using UnityEngine;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using TMPro;

public class NetworkClient : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public NetworkConnection m_Connection;
    public string serverIP;
    public ushort serverPort;

    public GameObject playerPrefab;
    private List<GameObject> playerList;
    private string myID;

    GameObject FindPlayerObject(string ID)
    {
        foreach (GameObject player in playerList)
        {
            if(player.GetComponentInChildren<TMP_Text>().text == ID)
            {
                return player;
            }
        }
        return null;
    }

    void Start()
    {
        m_Driver = NetworkDriver.Create();
        m_Connection = default(NetworkConnection);
        var endpoint = NetworkEndPoint.Parse(serverIP, serverPort);
        m_Connection = m_Driver.Connect(endpoint);

        playerList = new List<GameObject>();
    }

    void SendToServer(string message)
    {
        var writer = m_Driver.BeginSend(m_Connection);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message), Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    
    void OnConnect()
    {
        Debug.Log("We are now connected to the server");
        StartCoroutine(SendRepeatedHandshake());
        StartCoroutine(SendPlayerInfo());
    }

    IEnumerator SendRepeatedHandshake()
    {
        while(true)
        {
            yield return new WaitForSeconds(2);
            Debug.Log("Sending handshake");
            HandshakeMsg m = new HandshakeMsg();
            m.player.id = m_Connection.InternalId.ToString();
            SendToServer(JsonUtility.ToJson(m));
        }
    }
    IEnumerator SendPlayerInfo()
    {
        while (true)
        {
            PlayerUpdateMsg m = new PlayerUpdateMsg();

            GameObject playerObject = FindPlayerObject(myID);

            if (playerObject != null)
            {
                Debug.Log("Sending client info to server");
                m.player.id = myID;
                m.player.cubeColor = playerObject.GetComponent<MeshRenderer>().material.color;
                m.player.cubPos = playerObject.transform.position;

                SendToServer(JsonUtility.ToJson(m));
            }

            yield return new WaitForSeconds(0.03f);
        }
    }
    void OnData(DataStreamReader stream)
    {
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length, Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);
        Debug.Log("Got this header: " + header.cmd);

        switch (header.cmd)
        {
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!" + "Player Id:" + hsMsg.player.id);
                myID = hsMsg.player.id;
                break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
                break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
                UpdatePlayers(suMsg.players);
                break;
            case Commands.PLAYER_JOINED:
                PlayerUpdateMsg pjMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("PlayerJoined!, new player id: " + pjMsg.player.id);
                CreatePlayer(pjMsg.player);
                break;
            case Commands.PLAYER_LEFT:
                PlayerUpdateMsg plMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("PlayerLeft!, player id: " + plMsg.player.id);
                DestroyPlayer(plMsg.player);
                break;
            default:
                Debug.Log("Unrecognized message received!");
                break;
        }
    }
    
    private void DestroyPlayer(NetworkObjects.NetworkPlayer oldPlayer)
    {
        //Get the player cube in the game
        GameObject playerObject = FindPlayerObject(oldPlayer.id);

        if(playerObject != null)
        {
            //remove object from the list
            playerList.Remove(playerObject);

            //destroy the actor
            Destroy(playerObject);
        }
    }

    private void CreatePlayer(NetworkObjects.NetworkPlayer newPlayer)
    {
        // Create the newPlayer
        GameObject newPlayerObject = Instantiate(playerPrefab, newPlayer.cubPos, Quaternion.identity);
        // Set their ID tag
        newPlayerObject.GetComponentInChildren<TMP_Text>().text = newPlayer.id;
        newPlayerObject.GetComponent<MeshRenderer>().material.color = new Color(newPlayer.cubeColor.r, newPlayer.cubeColor.b, newPlayer.cubeColor.g);
        Debug.Log("playerList size: " + playerList.Count);
        //Check if the new player is us (the client)
        if (newPlayerObject.GetComponentInChildren<TMP_Text>().text == myID)
        {
            //Add in the movement component
            newPlayerObject.AddComponent<CubeBehaviour>();
        }
        // Add the new player to the list
        playerList.Add(newPlayerObject);

    }

    private void UpdatePlayers(List<NetworkObjects.NetworkPlayer> players)
    {
        foreach (var player in players)
        {
            GameObject playerObject = FindPlayerObject(player.id);

            if (playerObject != null)
            {
                // Set the position and color
                //playerObject.GetComponent<MeshRenderer>().material.color = new Color(player.cubeColor.r, player.cubeColor.b, player.cubeColor.g);
                playerObject.transform.position = player.cubPos;
            }

        }
    }

    void Disconnect()
    {
        m_Connection.Disconnect(m_Driver);
        m_Connection = default(NetworkConnection);
    }

    void OnDisconnect()
    {
        Debug.Log("Client got disconnected from server");
        m_Connection = default(NetworkConnection);
    }

    public void OnDestroy()
    {
        m_Driver.Dispose();
    }
    void Update()
    {
        m_Driver.ScheduleUpdate().Complete();

        if (!m_Connection.IsCreated)
        {
            Debug.Log("Something went wrong during connect");
            return;
        }

        DataStreamReader stream;
        NetworkEvent.Type cmd;
        cmd = m_Connection.PopEvent(m_Driver, out stream);
        while (cmd != NetworkEvent.Type.Empty)
        {
            if (cmd == NetworkEvent.Type.Connect)
            {
                OnConnect();
                
            }
            else if (cmd == NetworkEvent.Type.Data)
            {
                OnData(stream);

            }
            else if (cmd == NetworkEvent.Type.Disconnect)
            {
                OnDisconnect();
            }

            cmd = m_Connection.PopEvent(m_Driver, out stream);
        }
    }
}
