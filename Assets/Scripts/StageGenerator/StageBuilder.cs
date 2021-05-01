using String = System.String;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class StageBuilder : MonoBehaviour
{
    private HashSet<RoomPrefab> roomOptions;

    private StructureNode structureRoot;
    private RoomNode roomRoot;

    private int roomCount = 0;

    private GameObject doorPrefab;

    private StructureConfig config;

    void Start()
    {
        GameObject[] roomPrefabs = Resources.LoadAll<GameObject>("rooms");

        roomOptions = new HashSet<RoomPrefab>();

        foreach (GameObject roomPrefab in roomPrefabs)
            roomOptions.Add(new RoomPrefab(roomPrefab));

        doorPrefab = Resources.Load<GameObject>("Elements/test_door");

        config = new DefaultStructureConfig();

        GenerateStage();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.G))
        {
            GenerateStage();
        }
    }
    
    public void GenerateStage()
    {
        ClearRooms();
        GenerateStructure();
        GenerateRooms();
    }
    
    private void GenerateStructure()
    {
        Stack<StructureNode> dfsStack = new Stack<StructureNode>();

        StructureNode parentNode = new StructureNode(config.GetStructureType("start"), config, null);
        parentNode.SetChildCount();
        dfsStack.Push(parentNode);

        int roomCount = parentNode.GetChildCount();

        while (dfsStack.Count > 0)
        {
            StructureNode currentNode = dfsStack.Pop();

            int childCount = currentNode.GetChildCount();

            for (int i = 0; i < childCount; i++)
            {
                StructureNode childNode;

                if (roomCount < config.GetRoomLimit())
                {
                    childNode = currentNode.GenerateChild();
                }
                else
                {
                    childNode = new StructureNode(config.GetStructureType("deadEnd"), config, currentNode);
                }

                Debug.Log(childNode.GetStructureType() + " of " + roomCount + "/" + config.GetRoomLimit());
                currentNode.AddChild(childNode);
                childNode.SetChildCount();
                roomCount += childNode.GetChildCount();

                dfsStack.Push(childNode);
            }
        }

        structureRoot = parentNode;
    }

    private void GenerateRooms()
    {
        Stack<StructureNode> dfsStack = new Stack<StructureNode>();

        roomCount = 0;

        // Place a starting room (any qualifying end room) at the center of the scene.
        RoomPrefab rootRoomChoice = ChooseRoom(new HashSet<RoomPrefab>(), 1, structureRoot);
        roomRoot = CreateRoom(rootRoomChoice);
        structureRoot.SetRoomNode(roomRoot);

        dfsStack.Push(structureRoot);

        int iterationBreakCount = 0;

        while (dfsStack.Count > 0)
        {
            iterationBreakCount += 1;

            if (iterationBreakCount > 500)
            {
                break;
            }

            StructureNode currentNode = dfsStack.Pop();
            RoomNode currentRoom = currentNode.GetRoomNode();

            // Go through each door of the parent node and create a room for it.
            int roomsPutOnStack = 0;

            List<StructureNode> nonInstantiatedChildren = currentNode.GetNonInstantiatedChildren();
            List<DoorEdge> unconnectedChildDoors = currentRoom.GetUnconnectedChildDoors();
            // Debug.Log("Instantiating " + currentNode.GetStructureType() + " with children " + String.Join(", ", nonInstantiatedChildren) + " (" + nonInstantiatedChildren.Count + "/" + unconnectedChildDoors.Count + ")");

            for (int i = 0; i < unconnectedChildDoors.Count; i++)
            {
                DoorEdge door = unconnectedChildDoors[i];
                StructureNode childStructureNode = nonInstantiatedChildren[i];

                RoomPrefab newRoomPrefab = null;
                RoomNode newRoom = null;
                RoomNode failedRoom = null;
                HashSet<RoomPrefab> roomsTried = door.GetRoomsTried();

                while (roomsTried.Count < roomOptions.Count)
                {
                    newRoomPrefab = ChooseRoom(dfsStack.Count, roomsTried, childStructureNode);

                    if (newRoomPrefab == null)
                    {
                        newRoom = null;
                        break;
                    }
                    else
                    {
                        newRoom = CreateRoom(newRoomPrefab);
                        failedRoom = currentRoom.ConnectRoom(door, newRoom);

                        // Unable to fit new room by any of its doors
                        if (failedRoom != null)
                        {
                            roomsTried.Add(newRoomPrefab);
                            failedRoom.DestroyRoom();
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // If no room will work for this door, we need to choose a new room higher up the stack
                if (failedRoom != null || newRoom == null)
                {
                    for (int j = 0; j < roomsPutOnStack; j++)
                    {
                        dfsStack.Pop();
                    }

                    currentNode.SetRoomNodeNullRecursively();
                    dfsStack.Push(currentNode.GetParent());

                    RoomPrefab currentRoomPrefab = ChooseRoomByName(currentRoom.GetRoomObject().name);
                    currentRoom.GetParentDoor().GetRoomsTried().Add(currentRoomPrefab);
                    currentRoom.DestroyRoom();
                    break;
                }
                else
                {
                    childStructureNode.SetRoomNode(newRoom);
                    dfsStack.Push(childStructureNode);
                    roomsPutOnStack++;

                    // Add door
                    Instantiate(doorPrefab, door.GetParentDoorObject().transform.position, door.GetParentDoorObject().transform.rotation, newRoom.GetRoomObject().transform);
                }
            }

            FillSlots(currentRoom, currentNode);
        }
    }

    private void ClearRooms()
    {
        foreach (Transform childRoom in gameObject.GetComponentInChildren<Transform>())
        {
            childRoom.gameObject.SetActive(false);
            Object.Destroy(childRoom.gameObject);
        }
    }

    private RoomPrefab ChooseRoomByName(string roomName)
    {
        return roomOptions.ToList().Find(x => x.ToString() == roomName);
    }

    private RoomPrefab ChooseRoomByDoorCount(HashSet<RoomPrefab> roomsTried, int doorCount)
    {
        List<RoomPrefab> validRooms = roomOptions.Except(roomsTried).ToList().FindAll(x => x.GetDoorCount() == doorCount);

        if (validRooms.Count > 0)
        {
            RoomPrefab chosenRoom = validRooms[Random.Range(0, validRooms.Count)];
            return chosenRoom;
        }
        else
        {
            return null;
        }
    }

    private RoomPrefab ChooseRoom(int depth, HashSet<RoomPrefab> roomsTried, StructureNode structureNode)
    {
        if (depth >= 12)
        {
            return ChooseRoomByDoorCount(roomsTried, 1);
        }

        List<RoomPrefab> validRooms = roomOptions.Except(roomsTried).ToList().FindAll(x => x.GetPrefab().GetComponent<RoomConfig>().roomType.GetHashCode() == structureNode.GetRoomType().GetId());

        if (validRooms.Count > 0)
        {
            RoomPrefab chosenRoom = validRooms[Random.Range(0, validRooms.Count)];
            return chosenRoom;
        }
        else
        {
            return null;
        }
    }

    private RoomPrefab ChooseRoom(HashSet<RoomPrefab> roomsTried, int doorCount, StructureNode structureNode)
    {
        List<RoomPrefab> validRooms = roomOptions.Except(roomsTried).ToList().FindAll(x => x.GetDoorCount() == doorCount && x.GetPrefab().GetComponent<RoomConfig>().roomType.GetHashCode() == structureNode.GetRoomType().GetId());

        if (validRooms.Count > 0)
        {
            RoomPrefab chosenRoom = validRooms[Random.Range(0, validRooms.Count)];
            return chosenRoom;
        }
        else
        {
            return null;
        }
    }

    private RoomNode CreateRoom(RoomPrefab roomPrefab)
    {
        GameObject room = Instantiate(roomPrefab.GetPrefab(), new Vector3(0, 0, 0), Quaternion.identity, transform);
        room.name = room.name.Replace("(Clone)", "_" + roomCount);
        roomCount += 1;
        return new RoomNode(room);
    }

    private void FillSlots(RoomNode room, StructureNode structureNode)
    {
        foreach (RoomSlot slot in room.GetSlots())
        {
            GameObject slottedPrefab = null;

            if (room.GetParentRoom() == null)
            {
                slottedPrefab = slot.slottedPrefabs.Find(x => x.name == "player");
            }

            if (slottedPrefab != null)
            {
                GameObject slottedObject = Instantiate(slottedPrefab, slot.gameObject.transform.position, Quaternion.identity, transform);
                slottedObject.name = slottedObject.name.Replace("(Clone)", "");
            }
        }
    }
}
