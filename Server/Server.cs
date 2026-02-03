using System.Net;
using System.Net.Sockets;
using System.Text;



namespace BibliotekaServer
{
    public class Knjiga
    {
        
        public string Naslov { get; set; } = "";
        public string Autor { get; set; } = "";
        public int Kolicina { get; set; }
    }

    public class Program
    {
        // Portovi (mozes promeniti)
        private const int UDP_INFO_PORT = 5000;
        private const int TCP_PRISTUP_PORT = 6000;

        private static readonly List<Knjiga> _knjige = new();
        private static int _nextClientId = 10000; // “višecifren” ID

        static void Main(string[] args)
        {
            // 1) UDP INFO socket 
            Socket udpInfoSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpInfoSocket.Bind(new IPEndPoint(IPAddress.Any, UDP_INFO_PORT)); // Bind primer :contentReference[oaicite:6]{index=6}

            // Ispis IP/port (LocalEndPoint) 
            var udpLocal = udpInfoSocket.LocalEndPoint as IPEndPoint;
            Console.WriteLine($"UDP INFO utičnica aktivna na IP: {udpLocal?.Address}, PORT: {udpLocal?.Port}");

            // 2) TCP PRISTUP socket
            Socket tcpListenSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListenSocket.Bind(new IPEndPoint(IPAddress.Any, TCP_PRISTUP_PORT));
            tcpListenSocket.Listen(int.MaxValue); 

            var tcpLocal = tcpListenSocket.LocalEndPoint as IPEndPoint;
            Console.WriteLine($"TCP PRISTUPNA utičnica aktivna na IP: {tcpLocal?.Address}, PORT: {tcpLocal?.Port}");

            // 3) Dodavanje knjiga 
            Console.WriteLine();
            Console.WriteLine("========= Server biblioteke =========");
            Console.WriteLine("Komande:");
            Console.WriteLine("  1 - Dodaj knjigu");
            Console.WriteLine("  2 - Prikaži sve knjige");
            Console.WriteLine("  3 - Čekaj prijavu klijenta (TCP Accept)");
            Console.WriteLine("  0 - Izlaz");
            Console.WriteLine();

            while (true)
            {
                Console.Write("Unesi komandu: ");
                string? cmd = Console.ReadLine();

                if (cmd == "0")
                    break;

                if (cmd == "1")
                {
                    DodajKnjigu();
                }
                else if (cmd == "2")
                {
                    PrikaziKnjige();
                }
                else if (cmd == "3")
                {
                    PrihvatiPrijavuKlijenta(tcpListenSocket);
                }
                else
                {
                    Console.WriteLine("Nepoznata komanda.");
                }

                Console.WriteLine();
            }

            // Zatvaranje utičnica
            udpInfoSocket.Close();
            tcpListenSocket.Close();
        }

        private static void DodajKnjigu()
        {
            Console.Write("Naslov: ");
            string naslov = Console.ReadLine() ?? "";

            Console.Write("Autor: ");
            string autor = Console.ReadLine() ?? "";

            Console.Write("Količina: ");
            int kolicina;
            while (!int.TryParse(Console.ReadLine(), out kolicina))
            {
                Console.Write("Unesi ceo broj za količinu: ");
            }

            _knjige.Add(new Knjiga
            {
                Naslov = naslov,
                Autor = autor,
                Kolicina = kolicina
            });

            Console.WriteLine("Knjiga dodata u listu.");
        }

        private static void PrikaziKnjige()
        {
            if (_knjige.Count == 0)
            {
                Console.WriteLine("Lista knjiga je prazna.");
                return;
            }

            Console.WriteLine("=== Knjige (server) ===");
            for (int i = 0; i < _knjige.Count; i++)
            {
                var k = _knjige[i];
                Console.WriteLine($"{i + 1}. {k.Naslov} - {k.Autor} (Količina: {k.Kolicina})");
            }
        }

        private static void PrihvatiPrijavuKlijenta(Socket tcpListenSocket)
        {
            Console.WriteLine("Čekam TCP prijavu klijenta...");

            // Accept() 
            Socket acceptedSocket = tcpListenSocket.Accept();

            // Ispis udaljene adrese (RemoteEndPoint) 
            var remote = acceptedSocket.RemoteEndPoint as IPEndPoint;
            Console.WriteLine($"Klijent povezan sa: {remote?.Address}:{remote?.Port}");

            // Generiši jedinstveni višecifreni ID 
            int clientId = _nextClientId++;
            Console.WriteLine($"Dodeljujem klijentu ID: {clientId}");

            // Pošalji ID klijentu preko TCP (Send) 
            byte[] idBytes = Encoding.UTF8.GetBytes(clientId.ToString());
            acceptedSocket.Send(idBytes);

            // Po želji: zatvori konekciju (u zadatku 2 nije traženo dalje)
            acceptedSocket.Close();
            Console.WriteLine("ID poslat klijentu. Konekcija zatvorena.");
        }
    }
}