using System.Net;
using System.Net.Sockets;
using System.Text;


namespace BibliotekaKlijent
{
    public class Program
    {
        private const string SERVER_IP = "127.0.0.1";
        private const int TCP_PRISTUP_PORT = 6000;

        static void Main(string[] args)
        {
            // 1) Kreiranje TCP klijentske utičnice 
            Socket clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            // 2) Server endpoint 
            IPEndPoint serverEndPoint = new IPEndPoint(IPAddress.Parse(SERVER_IP), TCP_PRISTUP_PORT);

            // 3) Connect() 
            Console.WriteLine($"Povezujem se na server {SERVER_IP}:{TCP_PRISTUP_PORT} ...");
            clientSocket.Connect(serverEndPoint);
            Console.WriteLine("Prijava uspešna (TCP konekcija uspostavljena).");

            // 4) Prijem ID-a (Receive) 
            byte[] buffer = new byte[256];
            int bytesReceived = clientSocket.Receive(buffer);
            string idStr = Encoding.UTF8.GetString(buffer, 0, bytesReceived);

            // 5) "Čuvanje" ID-a (za sad u memoriji; ispis) 
            Console.WriteLine($"Dodeljen ID klijenta: {idStr}");
            Console.WriteLine("ID sačuvan lokalno (trenutno u memoriji aplikacije).");

            clientSocket.Close();
            Console.WriteLine("Kraj.");
        }
    }
}