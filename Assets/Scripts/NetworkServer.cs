using UnityEngine;
using UnityEngine.Assertions;
using Unity.Collections;
using Unity.Networking.Transport;
using NetworkMessages;
using System.Text;
using System.Collections;
using System.Collections.Generic;

public class NetworkServer : MonoBehaviour
{
    public NetworkDriver m_Driver;
    public ushort serverPort;
    private NativeList<NetworkConnection> m_Connections;
    private List<NetworkObjects.NetworkPlayer> m_PlayerList;

    private NetworkObjects.NetworkPlayer getPlayerFromList(string id)
    {
        foreach(var player in m_PlayerList)
        {
            if(player.id == id)
            {
                return player;
            }
        }
        return null;
    }

    void Start ()
    {
        m_Driver = NetworkDriver.Create();
        var endpoint = NetworkEndPoint.AnyIpv4;
        endpoint.Port = serverPort;
        if (m_Driver.Bind(endpoint) != 0)
            Debug.Log("Failed to bind to port " + serverPort);
        else
            m_Driver.Listen();
        m_Connections = new NativeList<NetworkConnection>(16, Allocator.Persistent);
        m_PlayerList = new List<NetworkObjects.NetworkPlayer>();

        StartCoroutine(SendHandShakeToAllClient());
        StartCoroutine(SendUpdateToAllClients());

    }

    IEnumerator SendHandShakeToAllClient()
    {
        while (true)
        {
            for (int i = 0; i < m_Connections.Length; i++)
            {
                if(!m_Connections[i].IsCreated)
                {
                    continue;
                }
                //Example to send a handshake message
                HandshakeMsg m = new HandshakeMsg();
                m.player.id = m_Connections[i].InternalId.ToString();
                SendToClient(JsonUtility.ToJson(m), m_Connections[i]);

            }
            yield return new WaitForSeconds(2);
        }
    }

    IEnumerator SendUpdateToAllClients()
    {
        while (true)
        {
            ServerUpdateMsg m = new ServerUpdateMsg();
            m.players = m_PlayerList;
            foreach (var connection in m_Connections)
            {
                if (!connection.IsCreated)
                    continue;
                // Send each client a server update
                SendToClient(JsonUtility.ToJson(m), connection);
            }
            yield return new WaitForSeconds(0.03f);
        }
    }

    void SendToClient(string message, NetworkConnection c){
        var writer = m_Driver.BeginSend(NetworkPipeline.Null, c);
        NativeArray<byte> bytes = new NativeArray<byte>(Encoding.ASCII.GetBytes(message),Allocator.Temp);
        writer.WriteBytes(bytes);
        m_Driver.EndSend(writer);
    }
    public void OnDestroy()
    {
        m_Driver.Dispose();
        m_Connections.Dispose();
    }

    void OnConnect(NetworkConnection c){
        m_Connections.Add(c);
        Debug.Log($"Accepted a connection, new id: {c.InternalId}");

        //example to send a handshake message
        HandshakeMsg m = new HandshakeMsg();
        m.player.id = c.InternalId.ToString();
        SendToClient(JsonUtility.ToJson(m), c);

        // create the player object info here and send it to everyone
        NetworkObjects.NetworkPlayer newPlayer = new NetworkObjects.NetworkPlayer();

        newPlayer.id = c.InternalId.ToString();
        newPlayer.cubeColor = UnityEngine.Random.ColorHSV(0f, 1f, 1f, 1f, 0.5f, 1f);
        newPlayer.cubePos = new Vector3(UnityEngine.Random.Range(-10, 10), UnityEngine.Random.Range(-10, 10), UnityEngine.Random.Range(0, 10));

        //Send the current players (via the connections list) the new player's info
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (m_Connections[i] != c)
            {
                PlayerUpdateMsg m2 = new PlayerUpdateMsg();
                m2.cmd = Commands.PLAYER_JOINED;
                m2.player = newPlayer;
                SendToClient(JsonUtility.ToJson(m2), m_Connections[i]);
            }
        }

        //The add the new player to the list
        m_PlayerList.Add(newPlayer);

        //finally send the new player all of the players to spawn(including itself)
        foreach (var player in m_PlayerList)
        {
            PlayerUpdateMsg m3 = new PlayerUpdateMsg();
            m3.cmd = Commands.PLAYER_JOINED;
            m3.player = player;
            SendToClient(JsonUtility.ToJson(m3), c);
            Debug.Log($"Send player join message to client id: { c.InternalId} with the command {m3.cmd}");
        }        
    }

    void OnData(DataStreamReader stream, int i){
        NativeArray<byte> bytes = new NativeArray<byte>(stream.Length,Allocator.Temp);
        stream.ReadBytes(bytes);
        string recMsg = Encoding.ASCII.GetString(bytes.ToArray());
        NetworkHeader header = JsonUtility.FromJson<NetworkHeader>(recMsg);

        switch(header.cmd){
            case Commands.HANDSHAKE:
                HandshakeMsg hsMsg = JsonUtility.FromJson<HandshakeMsg>(recMsg);
                Debug.Log("Handshake message received!");
            break;
            case Commands.PLAYER_UPDATE:
                PlayerUpdateMsg puMsg = JsonUtility.FromJson<PlayerUpdateMsg>(recMsg);
                Debug.Log("Player update message received!");
            break;
            case Commands.SERVER_UPDATE:
                ServerUpdateMsg suMsg = JsonUtility.FromJson<ServerUpdateMsg>(recMsg);
                Debug.Log("Server update message received!");
            break;
            default:
                Debug.Log("SERVER ERROR: Unrecognized message received!");
            break;
        }
    }

    private void UpdatePlayerInfo(NetworkObjects.NetworkPlayer player)
    {
        var playerOnList = getPlayerFromList(player.id);
        if (playerOnList != null)
        {
            playerOnList.cubeColor = player.cubeColor;
            playerOnList.cubePos = player.cubePos;
        }
        else
        {
            Debug.LogError("Tired to grab unknown player info, ID: " + player.id);
        }
    }

    void OnDisconnect(int i){
        Debug.Log("Client disconnected from server");
        m_Connections[i] = default(NetworkConnection);

        //send the disconnect message to all remaining clients
        PlayerUpdateMsg m = new PlayerUpdateMsg();
        m.cmd = Commands.PLAYER_LEFT;
        m.player = getPlayerFromList(m_Connections[i].InternalId.ToString());

        foreach (var client in m_Connections)
        {
            if (client != m_Connections[i])
            {
                SendToClient(JsonUtility.ToJson(m), client);
                Debug.Log($"Send player left message to client id: { client.InternalId} with the command {m.cmd}");
            }
        }
    }

    void Update ()
    {
        m_Driver.ScheduleUpdate().Complete();

        // CleanUpConnections
        for (int i = 0; i < m_Connections.Length; i++)
        {
            if (!m_Connections[i].IsCreated)
            {

                m_Connections.RemoveAtSwapBack(i);
                --i;
            }
        }

        // AcceptNewConnections
        NetworkConnection c = m_Driver.Accept();
        while (c  != default(NetworkConnection))
        {            
            OnConnect(c);

            // Check if there is another new connection
            c = m_Driver.Accept();
        }


        // Read Incoming Messages
        DataStreamReader stream;
        for (int i = 0; i < m_Connections.Length; i++)
        {
            Assert.IsTrue(m_Connections[i].IsCreated);
            
            NetworkEvent.Type cmd;
            cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            while (cmd != NetworkEvent.Type.Empty)
            {
                if (cmd == NetworkEvent.Type.Data)
                {
                    OnData(stream, i);
                }
                else if (cmd == NetworkEvent.Type.Disconnect)
                {
                    OnDisconnect(i);
                }

                cmd = m_Driver.PopEventForConnection(m_Connections[i], out stream);
            }
        }
    }
}