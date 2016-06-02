using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Collections;

namespace Chat_Server_Async
{
    class Program
    {
        static void Main(string[] args)
        {
            string name = Dns.GetHostName();
            int port = 42424;
            IPHostEntry host = Dns.GetHostEntry(name);
            IPAddress ipv4 = null;
            foreach (IPAddress address in host.AddressList)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork) // Ensure IP address is ipv4.
                {
                    ipv4 = address;
                    break;
                }
            }
            AsyncServer server = new AsyncServer(port, ipv4);
            server.StartServer();
            string runCondition = null;
            while(true)
            {
                runCondition = Console.ReadLine().ToUpper();
                if (runCondition == "EXIT")
                {
                    break;
                }
            }
        }
    }

    class AsyncServer
    {
        private int _port;
        private IPAddress _ip;
        UserList clients = new UserList();

        public AsyncServer(int port, IPAddress ip)
        {
            _port = port;
            _ip = ip;
        }

        public async void StartServer()
        {
            TcpListener listener = new TcpListener(_ip, _port); // Initialize TcpListener.
            listener.Start(); // Start listener.
            Console.WriteLine("Server is running on IP: {0} Port: {1}", _ip.ToString(), _port);
            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync(); // Connect to client. 
                    HandleConnections(client);
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                }
            }
        }

        private async void HandleConnections(TcpClient client)
        {
            NetworkStream stream = client.GetStream(); // Obtain network stream from client.
            User user = new User(client, stream); // Client and Stream are stored so they can be closed when client application is terminated.
            while (true)
            {
                try
                {
                    /* Multiple instances of WaitForMessages(pair) will be running on different 
                       task threads when multiple clients are connected to the server due to the
                       asynchronous nature of the server. */
                    await WaitForMessages(user); // Wait for new messages from current client.
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message);
                    user.Stream.Close(); // Close the stream.
                    user.Client.Close(); // Close the client.
                    string disconnectingUser = user.Name; // Obtain username of disconnected user.
                    string message = string.Format("{0}: {1} disconnected from the network", DateTime.Now.ToShortTimeString(), disconnectingUser);
                    byte[] messageBytes = Encoding.ASCII.GetBytes(message); // Encode disconnect message to byte array.
                    clients.RemoveUser(user); // Remove client from list of clients.
                    await SendReturnMessageAll(messageBytes, 1); // Send standard disconnect message to clients.
                    byte[] userBytes = Encoding.ASCII.GetBytes(disconnectingUser);
                    await SendReturnMessageAll(userBytes, 3); // Send User List disconnect message to clients.
                    break;
                }
            }
        }

        private async Task WaitForMessages(User user)
        {
            byte[] messageFlag = new byte[1];
            // Read just the first byte from stream to determine type of message.
            int x = await user.Stream.ReadAsync(messageFlag, 0, messageFlag.Length);
            switch (messageFlag[0])
            {
                case 1: // Initial connection to server from client.
                    await HandleInitialConnection(user);
                    break;
                case 2: // Standard chat message from client.
                    await HandleMessage(user);
                    break;
                case 3: // Private chat message from client.
                    await HandlePrivateMessage(user);
                    break;
                default: // Invalid signal.
                    Console.WriteLine("Invalid Message Flag");
                    break;
            }
        }

        private async Task HandleInitialConnection(User user)
        {
            string message = await ReadMessageBytes(user);
            user.SetName(message);
            byte[] username = Encoding.ASCII.GetBytes(message);
            await SendReturnMessageAll(username, 2); // Send message to update User list in clients, excluding new client.
            foreach (var client in clients) // Send all currently connected usernames to newly connected client.
            {
                byte[] clientName = Encoding.ASCII.GetBytes(client.Name);
                await SendReturnMessageOne(user, clientName, 2);
            }
            await SendReturnMessageOne(user, username, 2); // Needed because connecting client is not added to client list yet.
            clients.AddUser(user);
            string returnMessage = string.Format("{0}: User {1} connected!", DateTime.Now.ToShortTimeString(), message);
            byte[] returnBytes = Encoding.ASCII.GetBytes(returnMessage);
            await SendReturnMessageAll(returnBytes, 1); // Send standard message to clients to show new user connected.
        }

        private async Task HandleMessage(User user)
        {
            string message = await ReadMessageBytes(user);
            string sendingUser = user.Name;
            string returnMessage = string.Format("{0} {1}: {2}", DateTime.Now.ToShortTimeString(), sendingUser, message);
            byte[] returnBytes = Encoding.ASCII.GetBytes(returnMessage);
            await SendReturnMessageAll(returnBytes, 1); // Send message to all users.
        }

        private async Task HandlePrivateMessage(User user)
        {
            string message = await ReadMessageBytes(user);
            string sendingUsername = user.Name;
            string recievingUsername = await ReadMessageBytes(user);
            User recievingUser = clients.GetUserByName(recievingUsername);
            string returnMessageToRecieving = string.Format("{0}: Private Message From {1}: {2}", DateTime.Now.ToShortTimeString(), sendingUsername, message);
            string returnMessageToSending = string.Format("{0}: Private Message To {1}: {2}", DateTime.Now.ToShortTimeString(), recievingUsername, message);
            byte[] returnMessageToRecievingBytes = Encoding.ASCII.GetBytes(returnMessageToRecieving);
            byte[] returnMessageToSendingBytes = Encoding.ASCII.GetBytes(returnMessageToSending);
            await SendReturnMessageOne(recievingUser, returnMessageToRecievingBytes, 1);
            await SendReturnMessageOne(user, returnMessageToSendingBytes, 1);
        }

        private async Task SendReturnMessageAll(byte[] message, byte flag)
        {
            /* Index 0 in sendMessage reserved for message type flag, and indices 1-4 are reserved for
               the size of the string being sent. */
            byte[] sendMessage = new byte[message.Length + 5];
            sendMessage[0] = flag;
            byte[] messageSize = BitConverter.GetBytes(message.Length);
            messageSize.CopyTo(sendMessage, 1);
            message.CopyTo(sendMessage, 5);
            foreach (var user in clients)
            {
                await user.Stream.WriteAsync(sendMessage, 0, sendMessage.Length);
            }
        }

        private async Task SendReturnMessageOne(User user, byte[] message, byte flag)
        {
            /* Index 0 in sendMessage reserved for message type flag, and indices 1-4 are reserved for
               the size of the string being sent. */
            byte[] sendMessage = new byte[message.Length + 5];
            sendMessage[0] = flag;
            byte[] messageSize = BitConverter.GetBytes(message.Length);
            messageSize.CopyTo(sendMessage, 1);
            message.CopyTo(sendMessage, 5);
            await user.Stream.WriteAsync(sendMessage, 0, sendMessage.Length);
        }

        private async Task<string> ReadMessageBytes(User user)
        {
            int x, size;
            byte[] sizeBytes = new byte[4];
            // Read 4 bytes to determine message size.
            x = await user.Stream.ReadAsync(sizeBytes, 0, sizeBytes.Length); 
            size = BitConverter.ToInt32(sizeBytes, 0);
            byte[] buffer = new byte[size];
            string message = null;
            x = await user.Stream.ReadAsync(buffer, 0, buffer.Length);
            message += Encoding.ASCII.GetString(buffer);
            return message;
        }
    }
    
    class User
    {
        private TcpClient _client;
        private NetworkStream _stream;
        private string _name;
        private bool _nameSet = false;

        public User(TcpClient client, NetworkStream stream)
        {
            _client = client;
            _stream = stream;
        }

        public void SetName(string name)
        {
            if (!_nameSet)
            {
                _name = name;
                _nameSet = true;
            }
        }

        public TcpClient Client
        {
            get
            {
                return _client;
            }
        }

        public NetworkStream Stream
        {
            get
            {
                return _stream;
            }
        }

        public string Name
        {
            get
            {
                return _name;
            }
        }
    }

    class UserList : IEnumerable<User> // Implements IENumerable to allow foreach loop through items
    {
        private List<User> _users = new List<User>();

        public User GetUserByName(string name)
        {
            foreach (User user in _users)
            {
                if (user.Name == name)
                    return user;
            }
            return null;
        }

        public bool UsernameExistsInList(string name)
        {
            foreach (User user in _users)
            {
                if (user.Name == name)
                    return true;
            }
            return false;
        }

        public void AddUser(User user)
        {
            _users.Add(user);
        }

        public void RemoveUser(User user)
        {
            _users.Remove(user);
        }

        public IEnumerator<User> GetEnumerator()
        {
            return ((IEnumerable<User>)_users).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return ((IEnumerable<User>)_users).GetEnumerator();
        }
    }
}
