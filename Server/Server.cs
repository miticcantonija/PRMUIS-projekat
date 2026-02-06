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
        private static List<Iznajmljivanje> _iznajmljivanja = new List<Iznajmljivanje>();
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

            UcitajKnjigeIzFajla();
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

                                if (p.TipPoruke == "IZNAJMI")
                                {
                                    // Tražimo knjigu u listi
                                    var knjiga = _knjige.Find(k => k.Naslov.Equals(p.KnjigaPodaci.Naslov, StringComparison.OrdinalIgnoreCase));
                                    string odgovor = "NEUSPESNO";

                                    if (knjiga != null && knjiga.Kolicina > 0)
                                    {
                                        knjiga.Kolicina--; // Smanjujemo stanje

                                        // Kreiramo zapis o iznajmljivanju
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

                                    // ŠALJEMO ODGOVOR KLIJENTU (Ovo je novo!)
                                    byte[] odgovorBytes = Encoding.UTF8.GetBytes(odgovor);
                                    s.Send(odgovorBytes);
                                }
                                else if (p.TipPoruke == "VRATI")
                                {
                                    // 1. Pronađi knjigu u biblioteci da povećaš količinu
                                    var knjiga = _knjige.Find(k => k.Naslov.Equals(p.KnjigaPodaci.Naslov, StringComparison.OrdinalIgnoreCase));

                                    // 2. Pronađi i ukloni zapis iz liste iznajmljivanja
                                    // Tražimo zapis gde je taj klijent iznajmio tu knjigu
                                    var iznajmljivanje = _iznajmljivanja.Find(i => i.ClanID == p.KlijentID && i.KnjigaInfo.Contains(p.KnjigaPodaci.Naslov));

                                    if (knjiga != null)
                                    {
                                        knjiga.Kolicina++; // Vraćamo primerak u biblioteku
                                        if (iznajmljivanje != null) _iznajmljivanja.Remove(iznajmljivanje); // Brišemo iz evidencije

                                        Console.WriteLine($"\n[VRATI] Klijent {p.KlijentID} vratio knjigu: {knjiga.Naslov}");
                                        s.Send(Encoding.UTF8.GetBytes("VRACENO_OK"));
                                    }
                                    else
                                    {
                                        s.Send(Encoding.UTF8.GetBytes("GRESKA_NASLOV"));
                                    }
                                    SacuvajKnjigeUFajl();
                                }
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
            Console.Write("Kolicina: "); int.TryParse(Console.ReadLine(), out int kol);
            _knjige.Add(new Knjiga { Naslov = n, Autor = a, Kolicina= kol });
            SacuvajKnjigeUFajl();
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