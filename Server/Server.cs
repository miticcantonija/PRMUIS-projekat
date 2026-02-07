#pragma warning disable SYSLIB0011
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka.Modeli;

namespace BibliotekaServer
{
    public class Program
    {
        private const int UDP_PORT = 5000;
        private const int TCP_PORT = 6000;

        private static List<Knjiga> _knjige = new List<Knjiga>();
        private static List<Socket> _klijenti = new List<Socket>();
        private static List<Iznajmljivanje> _iznajmljivanja = new List<Iznajmljivanje>();
        private static int _nextClientId = 10550;

        static void Main(string[] args)
        {
            // 1. UDP SOKET - Polling model
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));
            udpSocket.Blocking = false;

            // 2. TCP LISTEN SOKET - Polling model
            Socket tcpListen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListen.Bind(new IPEndPoint(IPAddress.Any, TCP_PORT));
            tcpListen.Listen(10);
            tcpListen.Blocking = false;

            Console.WriteLine("========== SERVER BIBLIOTEKE (POLLING MODE) ==========");
            Console.WriteLine($"UDP Info port: {UDP_PORT}");
            Console.WriteLine($"TCP Pristupni port: {TCP_PORT}");
            Console.WriteLine("---------------------------------------");
            Console.WriteLine("Komande: [1] Dodaj rucno | [2] Lista | [0] Izlaz\n");

            UcitajKnjigeIzFajla();

            while (true)
            {
                // --- IZMENA 1: Polling za NOVE TCP KLIJENTE ---
                if (tcpListen.Poll(1000, SelectMode.SelectRead))
                {
                    Socket noviKlijent = tcpListen.Accept();
                    noviKlijent.Blocking = false;
                    _klijenti.Add(noviKlijent);

                    byte[] idBytes = Encoding.UTF8.GetBytes(_nextClientId.ToString());
                    noviKlijent.Send(idBytes);

                    Console.WriteLine($"\n[TCP] Povezan klijent. Dodeljen ID: {_nextClientId}");
                    _nextClientId++;
                }

                // --- IZMENA 2: Polling za UDP UPITE ---
                if (udpSocket.Poll(1000, SelectMode.SelectRead))
                {
                    EndPoint remoteEP = new IPEndPoint(IPAddress.Any, 0);
                    byte[] buffer = new byte[1024];
                    int rec = udpSocket.ReceiveFrom(buffer, ref remoteEP);

                    string zahtev = Encoding.UTF8.GetString(buffer, 0, rec);
                    string odgovor = ObradiUdpZahtev(zahtev);

                    byte[] odgovorBytes = Encoding.UTF8.GetBytes(odgovor);
                    udpSocket.SendTo(odgovorBytes, remoteEP);
                }

                // --- IZMENA 3: Polling za SVAKOG POVEZANOG KLIJENTA ---
                for (int i = _klijenti.Count - 1; i >= 0; i--)
                {
                    Socket s = _klijenti[i];
                    try
                    {
                        // Proveravamo da li klijent šalje nešto (timeout 1ms)
                        if (s.Poll(1000, SelectMode.SelectRead))
                        {
                            byte[] buffer = new byte[4096];
                            int primljeno = s.Receive(buffer);

                            if (primljeno == 0) // Klijent zatvorio vezu
                            {
                                Console.WriteLine("\n[TCP] Klijent se regularno odjavio.");
                                s.Close();
                                _klijenti.RemoveAt(i);
                                continue;
                            }

                            // Obrada primljene poruke
                            ObradiTcpPoruku(s, buffer, primljeno);
                        }
                    }
                    catch
                    {
                        Console.WriteLine("\n[TCP] Izgubljena veza sa klijentom.");
                        s.Close();
                        _klijenti.RemoveAt(i);
                    }
                }

                // RAD SA KONZOLOM SERVERA (Ostaje isto, ali sada brže reaguje)
                if (Console.KeyAvailable)
                {
                    var kljuc = Console.ReadKey(true).Key;
                    if (kljuc == ConsoleKey.D1) DodajKnjiguLokalno();
                    else if (kljuc == ConsoleKey.D2) PrikaziSveKnjige();
                    else if (kljuc == ConsoleKey.D0) return;
                }
            }
        }

        // Izdvojena logika radi preglednosti polling petlje
        private static void ObradiTcpPoruku(Socket s, byte[] buffer, int duzina)
        {
            using (MemoryStream ms = new MemoryStream(buffer, 0, duzina))
            {
                BinaryFormatter bf = new BinaryFormatter();
                Poruka p = (Poruka)bf.Deserialize(ms);

                if (p.TipPoruke == "IZNAJMI")
                {
                    var knjiga = _knjige.Find(k => k.Naslov.Equals(p.KnjigaPodaci.Naslov, StringComparison.OrdinalIgnoreCase));
                    string odgovor = "NEUSPESNO";

                    if (knjiga != null && knjiga.Kolicina > 0)
                    {
                        knjiga.Kolicina--;
                        Iznajmljivanje novo = new Iznajmljivanje
                        {
                            KnjigaInfo = $"{knjiga.Naslov} - {knjiga.Autor}",
                            ClanID = p.KlijentID,
                            DatumVracanja = DateTime.Now.AddDays(14).ToString("dd.MM.yyyy")
                        };
                        _iznajmljivanja.Add(novo);
                        odgovor = $"USPESNO|{novo.DatumVracanja}";
                        Console.WriteLine($"\n[IZNAJMI] Klijent {p.KlijentID} iznajmio: {knjiga.Naslov}");
                        SacuvajKnjigeUFajl();
                    }
                    s.Send(Encoding.UTF8.GetBytes(odgovor));
                }
                else if (p.TipPoruke == "VRATI")
                {
                    Knjiga? knjiga = null;
                    foreach (var k in _knjige)
                    {
                        if (k.Naslov.Equals(p.KnjigaPodaci.Naslov, StringComparison.OrdinalIgnoreCase))
                        {
                            knjiga = k;
                            break;
                        }
                    }

                    Iznajmljivanje? iznajmljivanje = null;
                    foreach (var izn in _iznajmljivanja)
                    {
                        // Proveravamo da li je to taj klijent I da li je to ta knjiga
                        if (izn.ClanID == p.KlijentID && izn.KnjigaInfo.Contains(p.KnjigaPodaci.Naslov))
                        {
                            iznajmljivanje = izn;
                            break;
                        }
                    }

                    if (knjiga != null)
                    {
                        knjiga.Kolicina++;
                        if (iznajmljivanje != null) _iznajmljivanja.Remove(iznajmljivanje);
                        Console.WriteLine($"\n[VRATI] Klijent {p.KlijentID} vratio knjigu: {knjiga.Naslov}");
                        s.Send(Encoding.UTF8.GetBytes("VRACENO_OK"));
                        SacuvajKnjigeUFajl();
                    }
                    else
                    {
                        s.Send(Encoding.UTF8.GetBytes("GRESKA_NASLOV"));
                    }
                }
            }
        }
        // --- POMOĆNE METODE ---
        private static void UcitajKnjigeIzFajla()
        {
            if (File.Exists("knjige_baza.txt"))
            {
                string[] linije = File.ReadAllLines("knjige_baza.txt");
                _knjige.Clear();
                foreach (var l in linije)
                {
                    var delovi = l.Split('|');
                    if (delovi.Length == 3)
                    {
                        _knjige.Add(new Knjiga
                        {
                            Naslov = delovi[0],
                            Autor = delovi[1],
                            Kolicina = int.Parse(delovi[2])
                        });
                    }
                }
                Console.WriteLine($"[BAZA] Učitano {_knjige.Count} knjiga iz fajla.");
            }
        }

        private static void SacuvajKnjigeUFajl()
        {
            List<string> linije = new List<string>();
            foreach (var k in _knjige)
            {
                // Čuvamo u formatu: Naslov|Autor|Kolicina
                linije.Add($"{k.Naslov}|{k.Autor}|{k.Kolicina}");
            }
            File.WriteAllLines("knjige_baza.txt", linije);
        }


        private static string ObradiUdpZahtev(string zahtev)
        {
            if (zahtev.StartsWith("PROVERA:"))
            {
                string naslov = zahtev.Substring(8).Trim();
                Knjiga? k = null;
                foreach (var stavka in _knjige)
                {
                    if (stavka.Naslov.Equals(naslov, StringComparison.OrdinalIgnoreCase))
                    {
                        k = stavka;
                        break; // Našli smo knjigu, prekidamo petlju
                    }
                }

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
            Console.Write("Naslov: ");
            string n = Console.ReadLine() ?? "";
            Console.Write("Autor: ");
            string a = Console.ReadLine() ?? "";
            Console.Write("Kolicina: "); 
            int.TryParse(Console.ReadLine(), out int kol);
            _knjige.Add(new Knjiga { Naslov = n, Autor = a, Kolicina= kol });
            SacuvajKnjigeUFajl();
            Console.WriteLine("Knjiga uspesno dodata.");
        }

        private static void PrikaziSveKnjige()
        {
            Console.WriteLine("\n--- Stanje u biblioteci ---");
            if (_knjige.Count == 0)
                Console.WriteLine("Nema knjiga.");
            else
            {
                foreach (var k in _knjige)
                {
                    Console.WriteLine($"[{k.Kolicina}] {k.Naslov} - {k.Autor}");
                }
            }
        }
    }
}