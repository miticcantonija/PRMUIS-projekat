#pragma warning disable SYSLIB0011
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka.Modeli; // Namespace tvoje zajedničke biblioteke

namespace BibliotekaServer
{
    public class Program
    {
        private const int UDP_PORT = 5000;
        private const int TCP_PORT = 6000;

        private static List<Knjiga> _knjige = new List<Knjiga>();
        private static List<Socket> _klijenti = new List<Socket>();
        private static int _nextClientId = 10550;

        static void Main(string[] args)
        {
            // 1. UDP SOKET - Za brze upite (provera i lista)
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));
            udpSocket.Blocking = false;

            // 2. TCP LISTEN SOKET - Za prihvatanje novih klijenata
            Socket tcpListen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListen.Bind(new IPEndPoint(IPAddress.Any, TCP_PORT));
            tcpListen.Listen(10);
            tcpListen.Blocking = false;

            Console.WriteLine("========== SERVER BIBLIOTEKE ==========");
            Console.WriteLine($"UDP Info port: {UDP_PORT}");
            Console.WriteLine($"TCP Pristupni port: {TCP_PORT}");
            Console.WriteLine("---------------------------------------");
            Console.WriteLine("Komande: [1] Dodaj rucno | [2] Lista | [0] Izlaz\n");

            while (true)
            {
                // Priprema liste za Select (osluškujemo listen soket, udp i sve povezane klijente)
                List<Socket> readList = new List<Socket> { tcpListen, udpSocket };
                readList.AddRange(_klijenti);

                // Čekamo 0.1s na mrežne događaje
                if (readList.Count > 0)
                    Socket.Select(readList, null, null, 100000);

                foreach (Socket s in readList)
                {
                    if (s == tcpListen) // NOVI KLIJENT SE POVEZUJE
                    {
                        Socket noviKlijent = s.Accept();
                        noviKlijent.Blocking = false;
                        _klijenti.Add(noviKlijent);

                        // Slanje ID-a klijentu kao string
                        byte[] idBytes = Encoding.UTF8.GetBytes(_nextClientId.ToString());
                        noviKlijent.Send(idBytes);

                        Console.WriteLine($"\n[TCP] Povezan klijent. Dodeljen ID: {_nextClientId}");
                        _nextClientId++;
                    }
                    else if (s == udpSocket) // UDP UPIT (PROVERA ILI LISTA)
                    {
                        EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                        byte[] buffer = new byte[1024];
                        int rec = s.ReceiveFrom(buffer, ref remoteEP);

                        string zahtev = Encoding.UTF8.GetString(buffer, 0, rec);
                        string odgovor = ObradiUdpZahtev(zahtev);

                        byte[] odgovorBytes = Encoding.UTF8.GetBytes(odgovor);
                        s.SendTo(odgovorBytes, remoteEP);
                    }
                    else // POSTOJEĆI KLIJENT ŠALJE KNJIGU (TCP)
                    {
                        try
                        {
                            byte[] buffer = new byte[4096];
                            int primljeno = s.Receive(buffer);
                            if (primljeno == 0) throw new Exception("Klijent se odjavio");

                            using (MemoryStream ms = new MemoryStream(buffer, 0, primljeno))
                            {
                                BinaryFormatter bf = new BinaryFormatter();
                                Poruka p = (Poruka)bf.Deserialize(ms);

                                _knjige.Add(p.KnjigaPodaci);
                                Console.WriteLine($"\n[TCP] Klijent {p.KlijentID} dodao knjigu: {p.KnjigaPodaci.Naslov}");
                            }
                        }
                        catch
                        {
                            Console.WriteLine("\n[TCP] Klijent je prekinuo vezu.");
                            s.Close();
                            _klijenti.Remove(s);
                            break;
                        }
                    }
                }

                // RAD SA KONZOLOM SERVERA
                if (Console.KeyAvailable)
                {
                    var kljuc = Console.ReadKey(true).Key;
                    if (kljuc == ConsoleKey.D1) DodajKnjiguLokalno();
                    else if (kljuc == ConsoleKey.D2) PrikaziSveKnjige();
                    else if (kljuc == ConsoleKey.D0) return;
                }
            }
        }

        // --- POMOĆNE METODE ---

        private static string ObradiUdpZahtev(string zahtev)
        {
            if (zahtev.StartsWith("PROVERA:"))
            {
                string naslov = zahtev.Substring(8).Trim();
                var k = _knjige.Find(x => x.Naslov.Equals(naslov, StringComparison.OrdinalIgnoreCase));

                if (k != null && k.Kolicina > 0)
                    return $"Knjiga '{k.Naslov}' postoji. Kolicina: {k.Kolicina}.";

                return "Knjiga nije pronadjena ili je kolicina 0.";
            }
            else if (zahtev == "LISTA:SVE")
            {
                var dostupne = _knjige.FindAll(x => x.Kolicina > 0);
                if (dostupne.Count == 0) return "Biblioteka je prazna.";

                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Dostupne knjige na stanju:");
                foreach (var k in dostupne)
                    sb.AppendLine($"- {k.Naslov} ({k.Autor}) | Kolicina: {k.Kolicina}");

                return sb.ToString();
            }
            return "Nepoznata UDP komanda.";
        }

        private static void DodajKnjiguLokalno()
        {
            Console.WriteLine("\n--- Rucni unos knjige ---");
            Console.Write("Naslov: "); string n = Console.ReadLine();
            Console.Write("Autor: "); string a = Console.ReadLine();
            _knjige.Add(new Knjiga { Naslov = n, Autor = a, Kolicina = 5 });
            Console.WriteLine("Knjiga uspesno dodata.");
        }

        private static void PrikaziSveKnjige()
        {
            Console.WriteLine("\n--- Stanje u biblioteci ---");
            if (_knjige.Count == 0) Console.WriteLine("Nema knjiga.");
            else _knjige.ForEach(k => Console.WriteLine($"[{k.Kolicina}] {k.Naslov} - {k.Autor}"));
        }
    }
}