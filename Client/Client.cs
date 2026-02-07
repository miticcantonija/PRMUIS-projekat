#pragma warning disable SYSLIB0011
using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka.Modeli; // Namespace tvoje zajedničke biblioteke

namespace BibliotekaKlijent
{
    class Program
    {
        private const int UDP_PORT = 5000;
        private const int TCP_PORT = 6000;
        private static int _mojID;

        static void Main(string[] args)
        {
            Console.WriteLine("========== KLIJENT BIBLIOTEKE ==========");

            // 1. POVEZIVANJE NA TCP SERVER
            Socket tcpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                tcpSocket.Connect(new IPEndPoint(IPAddress.Loopback, TCP_PORT));

                // Prva stvar: Server šalje dodeljeni ID
                byte[] idBuffer = new byte[1024];
                int primljenoID = tcpSocket.Receive(idBuffer);
                _mojID = int.Parse(Encoding.UTF8.GetString(idBuffer, 0, primljenoID));

                Console.WriteLine($"[SISTEM] Povezan na server. Vaš ID: {_mojID}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[GREŠKA] Nije moguće povezivanje sa serverom: " + ex.Message);
                return;
            }

            // 2. GLAVNI MENI
            while (true)
            {
                Console.WriteLine("\n--- MENI ---");
                Console.WriteLine("1. Pretraži knjigu (UDP)");
                Console.WriteLine("2. Lista svih dostupnih knjiga (UDP)");
                Console.WriteLine("3. Iznajmi knjigu (TCP)");
                Console.WriteLine("4. Vrati knjigu (TCP)");
                Console.WriteLine("5. Pregledaj moja iznajmljivanja (Lokalni fajl)");
                Console.WriteLine("0. Izlaz");
                Console.Write("Izbor: ");

                string? izbor = Console.ReadLine();

                switch (izbor)
                {
                    case "1": PretražiKnjiguUDP(); break;
                    case "2": PreuzmiListuUDP(); break;
                    case "3": IznajmiVratiTCP(tcpSocket, "IZNAJMI"); break;
                    case "4": IznajmiVratiTCP(tcpSocket, "VRATI"); break;
                    case "5": PrikaziMojaIznajmljivanja(_mojID); break;
                    case "0":
                        tcpSocket.Close();
                        return;
                    default:
                        Console.WriteLine("Nepostojeća opcija.");
                        break;
                }
            }
        }

        // --- UDP KOMUNIKACIJA (Polling model) ---
        private static void PošaljiUdpZahtev(string poruka)
        {
            Socket udp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            IPEndPoint serverEP = new IPEndPoint(IPAddress.Loopback, UDP_PORT);

            byte[] podaci = Encoding.UTF8.GetBytes(poruka);
            udp.SendTo(podaci, serverEP);

            // Čekamo odgovor maksimalno 1 sekundu (Polling)
            if (udp.Poll(1000000, SelectMode.SelectRead))
            {
                EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                byte[] buffer = new byte[2048];
                int primljeno = udp.ReceiveFrom(buffer, ref remoteEP);
                Console.WriteLine("\n[ODGOVOR SERVERA]:\n" + Encoding.UTF8.GetString(buffer, 0, primljeno));
            }
            else
            {
                Console.WriteLine("\n[INFO] Server (UDP) nije odgovorio. Proverite da li je upaljen.");
            }
            udp.Close();
        }

        private static void PretražiKnjiguUDP()
        {
            Console.Write("Unesite naslov za proveru: ");
            string ?naslov = Console.ReadLine();
            PošaljiUdpZahtev("PROVERA:" + naslov);
        }

        private static void PreuzmiListuUDP()
        {
            PošaljiUdpZahtev("LISTA:SVE");
        }

        // --- TCP KOMUNIKACIJA (Iznajmljivanje i Vraćanje) ---
        private static void IznajmiVratiTCP(Socket s, string tip)
        {
            Console.Write($"Unesite naslov knjige koju želite da {(tip == "IZNAJMI" ? "iznajmite" : "vratite")}: ");
            string naslov = Console.ReadLine() ?? "";

            Poruka p = new Poruka
            {
                TipPoruke = tip,
                KlijentID = _mojID,
                KnjigaPodaci = new Knjiga { Naslov = naslov }
            };

            try
            {
                // Serijalizacija i slanje
                BinaryFormatter bf = new BinaryFormatter();
                using (MemoryStream ms = new MemoryStream())
                {
                    bf.Serialize(ms, p);
                    s.Send(ms.ToArray());
                }

                // POLLING: Čekamo odgovor servera (max 2 sekunde)
                if (s.Poll(2000000, SelectMode.SelectRead))
                {
                    byte[] buffer = new byte[1024];
                    int primljeno = s.Receive(buffer);
                    string odgovor = Encoding.UTF8.GetString(buffer, 0, primljeno);

                    if (odgovor.StartsWith("USPESNO"))
                    {

                        string datum = odgovor.Contains("|") ? odgovor.Split('|')[1] : "nepoznat";
                        Console.WriteLine($"\n[INFO] Uspešno iznajmljeno! Rok: {datum}");
                        SacuvajLokalno(naslov, datum, _mojID);
                    }
                    else if (odgovor == "VRACENO_OK")
                    {
                        Console.WriteLine("\n[INFO] Knjiga uspešno vraćena u biblioteku.");
                        UkloniLokalno(naslov,_mojID);
                    }
                    else if (odgovor == "NEUSPESNO")
                    {
                        Console.WriteLine("\n[INFO] Operacija neuspešna. Proverite stanje knjige.");
                    }
                    else
                    {
                        Console.WriteLine("\n[SERVER]: " + odgovor);
                    }
                }
                else
                {
                    Console.WriteLine("\n[GREŠKA] Server nije odgovorio na TCP zahtev.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n[GREŠKA] Komunikacija prekinuta: " + ex.Message);
            }
        }

        private static void SacuvajLokalno(string naslov, string datum, int mojID)
        {
            // Fajl će se zvati npr. "iznajmljeno_10550.txt"
            string putanja = $"iznajmljeno_{mojID}.txt";
            File.AppendAllLines(putanja, new[] { $"{naslov} | Rok: {datum}" });
        }

        private static void UkloniLokalno(string naslov, int mojID)
        {
            string putanja = $"iznajmljeno_{mojID}.txt";
            if (!File.Exists(putanja)) return;

            var linije = File.ReadAllLines(putanja);
            List<string> noveLinije = new List<string>();

            bool obrisano = false;
            foreach (var l in linije)
            {
                if (!obrisano && l.StartsWith(naslov, StringComparison.OrdinalIgnoreCase))
                {
                    obrisano = true;
                    continue;
                }
                noveLinije.Add(l);
            }
            File.WriteAllLines(putanja, noveLinije);
        }

        private static void PrikaziMojaIznajmljivanja(int mojID)
        {
            string putanja = $"iznajmljeno_{mojID}.txt";
            Console.WriteLine($"\n--- MOJE KNJIGE (ID: {mojID}) ---");

            if (!File.Exists(putanja) || new FileInfo(putanja).Length == 0)
            {
                Console.WriteLine("Nemate iznajmljenih knjiga.");
                return;
            }

            string[] knjige = File.ReadAllLines(putanja);
            foreach (var k in knjige) Console.WriteLine("- " + k);
        }
    }
}