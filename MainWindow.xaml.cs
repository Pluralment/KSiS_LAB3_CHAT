using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;
using System.Windows;
using System.Net;
using System.Net.Sockets;
using System.Windows.Input;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Collections.ObjectModel;
using System.Windows.Threading;

namespace Lab3
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        // Список IP-адресов, подсоединенных к чату.
        private ObservableCollection<PublicUserInfo> UserIpList = new ObservableCollection<PublicUserInfo>();
        static protected internal int RemotePort = 8000;
        static protected internal int LocalPort = 8000;
        // Список локальных IP-адресов, закрепленных за данным узлом.
        public IPAddress[] LocalIPAdressList;
        // Локальный IP.
        public IPAddress LocalIp;

        // Поток, отвечающий за приём broadcast-пакетов.
        public Task BroadcastTask = null;
        
        public delegate void OneArgDelegate(string arg);

        public delegate void objectDelegate(PublicUserInfo arg);

        public delegate void IpDelegate(IPEndPoint arg, string name);

        //public string localIpString;

        //-----------------------------------------------------
        // Сервер.
        static ServerObject tcpServer = new ServerObject();
        // Задача для прослушивания входящих подключений.
        static Task listenTask; 
        static string LocalUserName = "userName";
        private const int tcpPort = 8888;
        static NetworkStream stream;
        //-----------------------------------------------------

        public MainWindow()
        {
            InitializeComponent();

            // Получение IP-адресов, принадлежащих данному пользователю.
            LocalIPAdressList = GetLocalIPList();

            UserListBox.ItemsSource = UserIpList;

            // Задание имени по умолчанию.
            NameBox.Text = "USER";
        }

        private void EnterChat(object sender, RoutedEventArgs e)
        {        
            try
            {
                // Старт задачи, прослушивающей входящие broadcast-пакеты.
                BroadcastTask = Task.Run(() => ReceiveNewUserId());

                listenTask = Task.Run(() => Listen());
            }
            catch (Exception ex)
            {
                //server.Disconnect();
                MessageBox.Show(ex.Source + ": " + ex.Message, "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning,
                                MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
            Thread.Sleep(10);

            LocalUserName = NameBox.Text;
            // Широковещательный пакет, объявляющий о том, что данный пользователь вошёл в сеть.
            SendIdData(LocalUserName, ChatMessage.CONNECT_MSG);
            Thread.Sleep(10);

            ConnectBtn.IsEnabled = false;
            DisconnectBtn.IsEnabled = true;
            NameBox.IsEnabled = false;
            SendMsgBtn.IsEnabled = true;
        }

        private void LeaveChat(object sender, RoutedEventArgs e)
        {
            try
            {
                SendIdData("", ChatMessage.BREAK_MSG);

                foreach (var user in tcpServer.clients)
                {
                    if (user.Stream != null)
                        user.Stream.Close();
                    if (user.client != null)
                        user.client.Close();
                    if (user != null)
                        user.Close();                 
                }
                tcpServer.clients.Clear();

                ConnectBtn.IsEnabled = true;
                DisconnectBtn.IsEnabled = false;
                NameBox.IsEnabled = true;
                SendMsgBtn.IsEnabled = false;

                if (stream != null)
                    stream.Close();

                // Остановка TCP - сервера.
                tcpServer.tcpListener.Stop();

                // Очищение графического списка активных соединений.
                UserIpList.Clear();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex.Message + ": LeaveChat", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        private void InputBox_MouseUp(object sender, MouseButtonEventArgs e)
        {
            InputBox.Text = "";
        }

        public void UpdateChatBox(string message)
        {
            string str = "\n" + message;
            ChatBox.Text += str;
        }

        public void UpdateHistoryConnectionBox(string message)
        {
            HistoryConnectionBox.Text += message + "\n";
        }

        // Рассылка broadcast-датаграм.
        public void SendIdData(string localIp, byte MsgType)
        {
            UdpClient sender = new UdpClient(); 
            sender.EnableBroadcast = true;
            var broadcastAddress = new IPEndPoint(IPAddress.Parse("192.168.1.255"), RemotePort);

            string message = localIp;
            var MessageObject = new ChatMessage(message, MsgType);
            byte[] data = MessageObject.ToBytes();
            try
            {
                sender.Send(data, data.Length, broadcastAddress); 
            }
            catch (Exception e)
            {
                MessageBox.Show(e.InnerException + ": " + e.Message + ": SendIdData", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
            finally
            {
                sender.Close();
                sender.Dispose();
            }
        }

        public bool LookUpUserIpList(string Ip)
        {
            foreach(PublicUserInfo LocalIp in UserIpList)
            {
                if (LocalIp.IP == Ip)            
                    return true;          
            }
            return false;
        }
      
        public bool LookUpLocalIpList(string Ip)
        {
            foreach (IPAddress LocalIp in LocalIPAdressList)
            {
                if (LocalIp.ToString() == Ip)
                    return true;
            }
            return false;
        }

        // Прослушка входящих tcp-соединений.
        public void Listen()
        {     
            try
            {
                tcpServer.tcpListener = new TcpListener(IPAddress.Any, tcpPort);
                tcpServer.tcpListener.Start();            

                while (true)
                {
                    TcpClient tcpClient = tcpServer.tcpListener.AcceptTcpClient();             
                    // Создание сущности нового пользователя, добавление пользователя в список активных TCP-подключений.
                    ClientObject clientObject = new ClientObject(tcpClient, tcpServer);
                    Task clientTask = Task.Run(() => Process(clientObject));
                }
            }
            catch (SocketException ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex.Message + " - SocketException: ServerObject.Listen", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                if (tcpServer.tcpListener != null)
                {
                    tcpServer.tcpListener.Stop();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex.Message + " - Exception: ServerObject.Listen", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);

                for (int i = 0; i < tcpServer.clients.Count; i++)
                {
                    tcpServer.clients[i].Close(); //отключение клиента
                }
                tcpServer.clients.Clear();

                // Остановка TCP - сервера.
                if (tcpServer.tcpListener != null)
                    tcpServer.tcpListener.Stop();
            }         
        }

        public TcpClient CreateTcpConnection(IPAddress Ip, string Name)
        {
            var NewTcpClient = new TcpClient();
            try
            {
                IPEndPoint endPoint = new IPEndPoint(Ip, tcpPort);
                NewTcpClient.Connect(endPoint);

                // Создание сообщения.
                var MessageObject = new ChatMessage(Name, ChatMessage.CONNECT_MSG);
                byte[] data = MessageObject.ToBytes();

                var newStream = NewTcpClient.GetStream();

                // Send the message to the connected TcpServer.
                // Отправка имени для установления TCP-соединения.
                newStream.Write(data, 0, data.Length);

                // Создание сущности нового пользователя, добавление пользователя в список активных TCP-подключений.
                ClientObject clientObject = new ClientObject(NewTcpClient, tcpServer);
                Task clientTask = Task.Run(() => Process(clientObject));

                return NewTcpClient;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex + "  - CreateTcpConnection", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                return null;
            }
        }
 
        public void Process(ClientObject clientObject)
        {
            clientObject.userName = "";
            try
            {
                clientObject.Stream = clientObject.client.GetStream();

                // В бесконечном цикле получаем сообщения от клиента.
                while (true)
                {
                    try
                    {
                        // Получаем сообщения, входящие от других пользователей.
                        var messageObject = clientObject.GetMessage();
                        if (messageObject == null)
                        {
                            //clientObject.server.RemoveConnection(clientObject.Id);
                            break;
                        }

                        if (messageObject.Code == ChatMessage.CONNECT_MSG)
                        {
                            clientObject.userName = messageObject.Message;
                            // Добавление новой строчки в UserIpList.
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new IpDelegate(DispatcherAddUserListBoxElement), 
                                                   (IPEndPoint)(clientObject.client.Client).RemoteEndPoint, clientObject.userName);
                            // Обновление истории подключений.
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new OneArgDelegate(UpdateHistoryConnectionBox),
                                                   clientObject.userName + " joined chat");
                        }

                        if (messageObject.Code == ChatMessage.CHAT_MSG)
                        {
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new OneArgDelegate(UpdateChatBox),
                                                    messageObject.Message);
                        }
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show(ex + ": ИСКЛЮЧЕНИЕ В ClientObject.Process()", "Внимание", MessageBoxButton.OK,
                                        MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex.Message + " - ClientObject.Process", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
            finally
            {
                // В случае выхода из цикла закрываем ресурсы.
                clientObject.server.RemoveConnection(clientObject.Id);
                //clientObject.Close();             
            }
        } 
        

        public void AddUserListBoxElement(IPEndPoint Ip, string Name)
        {
            // Создание новой записи в UserListBox.
            UserIpList.Add(new PublicUserInfo
            {
                Name = Name,
                IP = Ip.Address.ToString(),
                endPoint = Ip
            });
        }

        public void DispatcherAddUserListBoxElement(IPEndPoint Ip, string Name)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new IpDelegate(AddUserListBoxElement), Ip, Name);
        }

        public void AddNewUser(IPEndPoint Ip, string Name)
        {
            try
            {
                AddUserListBoxElement(Ip, Name);
                if (!LookUpLocalIpList(Ip.Address.ToString()))
                {
                    // Подключение к удаленному узлу.
                    var tcpClient = CreateTcpConnection(Ip.Address, LocalUserName);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.InnerException + ": " + ex + " - AddNewUser", "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
        }

        public void RemoveUserListBoxElement(PublicUserInfo userData)
        {
            UserIpList.Remove(userData);
        }

        // Принимает broadcast-датаграмы. 
        public void ReceiveNewUserId()
        {
            UdpClient receiver = new UdpClient(LocalPort);
            // Адрес входящего подключения.
            IPEndPoint remoteIp = new IPEndPoint(IPAddress.Any, LocalPort); 
            try
            {
                while (true)
                {
                    byte[] data = receiver.Receive(ref remoteIp);
                    var MessageObject = new ChatMessage(data);

                    // Получен broadcast-пакет прерывания сессии.
                    if (MessageObject.Code == ChatMessage.BREAK_MSG)
                    {                    
                        // Если отправил я.
                        if (LookUpLocalIpList(remoteIp.Address.ToString()))
                        {
                            MessageBox.Show("You left the session", "Внимание", MessageBoxButton.OK,
                                            MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                            break;
                        }   
                        else
                        {
                            foreach (var userData in UserIpList)
                            {
                                var IpAddress = userData.endPoint.Address.ToString();
                                if (IpAddress == remoteIp.Address.ToString())
                                {
                                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new OneArgDelegate(UpdateHistoryConnectionBox),
                                                           userData.Name + " left the chat");
                                    Dispatcher.BeginInvoke(DispatcherPriority.Normal, new objectDelegate(RemoveUserListBoxElement), userData);
                                    break;
                                }
                            }
                        }
                    }
                    if (MessageObject.Code == ChatMessage.CONNECT_MSG)
                    {               
                        if (!LookUpUserIpList(remoteIp.Address.ToString()))
                        {
                            if (LookUpLocalIpList(remoteIp.Address.ToString()))
                                LocalIp = remoteIp.Address;
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new OneArgDelegate(UpdateHistoryConnectionBox),
                                                   MessageObject.Message + " entered the chat");
                            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new IpDelegate(AddNewUser), remoteIp, MessageObject.Message);
                        }
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.InnerException + ": " + e.Message, "Внимание", MessageBoxButton.OK,
                                MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
            }
            finally
            {
                receiver.Close();
                receiver.Dispose();
            }
        }

        // Получение списка локальных IP-адресов.
        private IPAddress[] GetLocalIPList()
        {
            var host = Dns.GetHostEntry(Dns.GetHostName());
            return host.AddressList;
        }

        private void SendChatMessage(object sender, RoutedEventArgs e)
        {
            string message = LocalUserName + ": " + InputBox.Text + "\n";
            ChatBox.Text += message;
            tcpServer.BroadcastMessage(message, ChatMessage.CHAT_MSG);
        }
    }

    // Содержит публичную информацию о каждом клиенте в сети.
    public class PublicUserInfo : INotifyPropertyChanged
    {
        private string name;
        public string Name
        {
            get => name;
            set
            {
                name = value;
                // Уведомляем об изменении свойства.
                NotifyPropertyChanged();
            }
        }
        public IPEndPoint endPoint;
        //public ClientObject clientObject;
        private string ip;
        public string IP
        {
            get => ip;
            set 
            {
                ip = value; 
                NotifyPropertyChanged(); 
            }
        }

        #region Реализация INPC — обычно выносится в отдельный базовый класс
        private void NotifyPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public event PropertyChangedEventHandler PropertyChanged;
        #endregion
    }

    // Класс tcp-сервера.
    public class ServerObject
    {
        // Сервер для прослушивания.
        public TcpListener tcpListener;
        // Список всех активных подключений.
        public List<ClientObject> clients = new List<ClientObject>(); 

        protected internal void AddConnection(ClientObject clientObject)
        {
            clients.Add(clientObject);
        }

        protected internal void RemoveConnection(string id)
        {
            // Получаем по id закрытое подключение.
            ClientObject client = clients.FirstOrDefault(c => c.Id == id);
     
            // Удаляем его из списка подключений.
            if (client != null)
            {
                string Name = client.userName;
                if (clients.Remove(client))
                {
                    MessageBox.Show(Name + " отключился от сети - получено из ServerObject.RemoveConnection(string id)", "Внимание", MessageBoxButton.OK,
                                    MessageBoxImage.Information, MessageBoxResult.OK, MessageBoxOptions.DefaultDesktopOnly);
                };
            }
        }

        // Трансляция сообщения подключенным клиентам.
        protected internal void BroadcastMessage(string message, byte MessageType)
        {           
            ChatMessage messageObject = new ChatMessage(message, MessageType);
            byte[] data = messageObject.ToBytes();
            for (int i = 0; i < clients.Count; i++)
            {
                clients[i].Stream.Write(data, 0, data.Length);  
            }
        }

        // Отключение всех клиентов.
        protected internal void Disconnect()
        {
            // Остановка сервера.
            tcpListener.Stop(); 
            for (int i = 0; i < clients.Count; i++)
            {
                // Отключение клиента.
                clients[i].Close(); 
            }
        }
    }
    
    public class ClientObject
    {
        protected internal string Id { get; private set; }
        public NetworkStream Stream { get; set; }      
        // Имя клиента.
        public string userName;
        // Объект клиента.
        public TcpClient client;
        // Объект сервера.
        public ServerObject server; 

        public ClientObject(TcpClient tcpClient, ServerObject serverObject)
        {
            Id = Guid.NewGuid().ToString();
            client = tcpClient;
            server = serverObject;
            serverObject.AddConnection(this);
        }

        // Чтение входящего сообщения.
        public ChatMessage GetMessage()
        {
            ChatMessage messageObject;
            try
            {
                byte[] data = new byte[256];
                int bytes = 0;
                do
                {
                    bytes = Stream.Read(data, 0, data.Length);
                }
                while (Stream.DataAvailable);

                messageObject = new ChatMessage(data);

                return messageObject;
            }
            catch (Exception)
            {             
                Close();
                return null;
            }
        }

        // Закрытие подключения.
        protected internal void Close()
        {
            if (Stream != null)
            {
                Stream.Close();
            }             
            if (client != null)
            {
                client.Close();
            }
        }
    }
    
    public class ChatMessage
    {
        public const byte BREAK_MSG   = 0;
        public const byte LEAVE_MSG   = 0;
        public const byte CHAT_MSG    = 1;
        public const byte CONNECT_MSG = 2;
        private protected const int MESSAGE_LEN_SIZE = 2;

        // Код (тип) сообщения.
        public byte Code;
        // Длина сообщения.
        public ushort MessageLength;
        // Сообщение.
        public string Message;
        // Передаваемое сообщение в байтовом массиве.
        public byte[] MsgData;

        // Конструктор для преобразования входящего сообщения.
        public ChatMessage(byte[] data)
        {
            Code = data[0];
            //MessageLength = Convert.ToUInt16(Encoding.ASCII.GetString(data, 1, MESSAGE_LEN_SIZE)); // НЕВЕРНЫЙ ФОРМАТ!!!
            MessageLength = BitConverter.ToUInt16(data, 1); ; 
            Message = Encoding.ASCII.GetString(data, 3, MessageLength);
        }

        // Конструктор для формирования исходящего сообщения.
        public ChatMessage(string message, byte MsgType)
        {
            Code = MsgType;
            switch(Code)
            {
                // 0 - LEAVE_MSG или BREAK_MSG
                case BREAK_MSG:
                    Message = "";
                    MessageLength = 0;
                    MsgData = Encoding.ASCII.GetBytes(Message);
                    break;
                // 1 - CHAT_MSG
                case CHAT_MSG:
                    Message = message;
                    MessageLength = Convert.ToUInt16(Message.Length);
                    MsgData = Encoding.ASCII.GetBytes(Message);
                    break;
                // 2 - CONNECT_MSG
                case CONNECT_MSG:
                    Message = message;
                    MessageLength = Convert.ToUInt16(Message.Length);
                    MsgData = Encoding.ASCII.GetBytes(Message);
                    break;
            }
        }

        // Возвращает сообщение в виде массива байтов.
        public byte[] ToBytes()
        {
            byte[] data = new byte[1 + MESSAGE_LEN_SIZE + MessageLength];
            Buffer.BlockCopy(BitConverter.GetBytes(Code), 0, data, 0, 1);
            Buffer.BlockCopy(BitConverter.GetBytes(MessageLength), 0, data, 1, MESSAGE_LEN_SIZE);
            Buffer.BlockCopy(MsgData, 0, data, (MESSAGE_LEN_SIZE + 1), MessageLength);
            return data;
        }
    }
}
