#pragma warning disable SYSLIB0011
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka.Modeli;

namespace BibliotekaServer
{
    public class Program
    {
        private const int UDP_PORT = 5000;
        private const int TCP_PORT = 6000;

        private const string KNJIGE_FAJL = "knjige_baza.txt";
        private const string IZN_FAJL = "iznajmljivanja_baza.txt"; 

        private static List<Knjiga> _knjige = new List<Knjiga>();
        private static List<Socket> _klijenti = new List<Socket>();
        private static List<Iznajmljivanje> _iznajmljivanja = new List<Iznajmljivanje>();

        private static int _nextClientId = 10550;

        static void Main(string[] args)
        {
            // 1) UDP socket
            Socket udpSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
            udpSocket.Bind(new IPEndPoint(IPAddress.Any, UDP_PORT));
            udpSocket.Blocking = false;

            // 2) TCP listen socket
            Socket tcpListen = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            tcpListen.Bind(new IPEndPoint(IPAddress.Any, TCP_PORT));
            tcpListen.Listen(10);
            tcpListen.Blocking = false;

            Console.WriteLine("========== SERVER BIBLIOTEKE (POLLING MODE) ==========");
            Console.WriteLine($"UDP Info port: {UDP_PORT}");
            Console.WriteLine($"TCP Pristupni port: {TCP_PORT}");
            Console.WriteLine("---------------------------------------");
            Console.WriteLine("Komande: [1] Dodaj rucno | [2] Lista | [0] Izlaz\n");

            // ucitaj stanje na startu 
            UcitajKnjigeIzFajla();
            UcitajIznajmljivanjaIzFajla();

            // samo informativno na serveru
            IspisiKasnjenjaNaServeru();

            while (true)
            {
                // Novi TCP klijenti
                if (tcpListen.Poll(1000, SelectMode.SelectRead))
                {
                    Socket noviKlijent = tcpListen.Accept();
                    noviKlijent.Blocking = false;
                    _klijenti.Add(noviKlijent);

                    int dodeljenId = _nextClientId++;
                    noviKlijent.Send(Encoding.UTF8.GetBytes(dodeljenId.ToString()));

                    Console.WriteLine($"\n[TCP] Povezan klijent. Dodeljen ID: {dodeljenId}");

                    // ako klijent kasni, javi mu odmah
                    PosaljiKasnjenjaKlijentuAkoPostoje(noviKlijent, dodeljenId);
                }

                // UDP upiti
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

                // TCP poruke od povezanih klijenata
                for (int i = _klijenti.Count - 1; i >= 0; i--)
                {
                    Socket s = _klijenti[i];
                    try
                    {
                        if (s.Poll(1000, SelectMode.SelectRead))
                        {
                            byte[] buffer = new byte[4096];
                            int primljeno = s.Receive(buffer);

                            if (primljeno == 0)
                            {
                                Console.WriteLine("\n[TCP] Klijent se regularno odjavio.");
                                s.Close();
                                _klijenti.RemoveAt(i);
                                continue;
                            }

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

                // Server konzola komande
                if (Console.KeyAvailable)
                {
                    var kljuc = Console.ReadKey(true).Key;
                    if (kljuc == ConsoleKey.D1) DodajKnjiguLokalno();
                    else if (kljuc == ConsoleKey.D2) PrikaziSveKnjige();
                    else if (kljuc == ConsoleKey.D0) return;
                }
            }
        }

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

                        var novo = new Iznajmljivanje
                        {
                            ClanID = p.KlijentID,
                            KnjigaInfo = $"{knjiga.Naslov}|{knjiga.Autor}", 
                            DatumVracanja = DateTime.Now.AddDays(14).ToString("dd.MM.yyyy")
                        };

                        _iznajmljivanja.Add(novo);

                        SacuvajKnjigeUFajl();
                        SacuvajIznajmljivanjaUFajl(); 

                        odgovor = $"USPESNO|{novo.DatumVracanja}";
                        Console.WriteLine($"\n[IZNAJMI] Klijent {p.KlijentID} iznajmio: {knjiga.Naslov}");
                    }

                    s.Send(Encoding.UTF8.GetBytes(odgovor));
                }
                else if (p.TipPoruke == "VRATI")
                {
                    var knjiga = _knjige.Find(k => k.Naslov.Equals(p.KnjigaPodaci.Naslov, StringComparison.OrdinalIgnoreCase));
                    if (knjiga == null)
                    {
                        s.Send(Encoding.UTF8.GetBytes("GRESKA_NASLOV"));
                        return;
                    }

                    // nadjii iznajmljivanje za tog klijenta i tu knjigu
                    Iznajmljivanje? izn = null;
                    for (int i = 0; i < _iznajmljivanja.Count; i++)
                    {
                        var item = _iznajmljivanja[i];
                        if (item.ClanID == p.KlijentID)
                        {
                            // KnjigaInfo format: "Naslov|Autor"
                            var parts = item.KnjigaInfo.Split('|');
                            if (parts.Length >= 1 && parts[0].Equals(knjiga.Naslov, StringComparison.OrdinalIgnoreCase))
                            {
                                izn = item;
                                break;
                            }
                        }
                    }

                    knjiga.Kolicina++;

                    if (izn != null)
                        _iznajmljivanja.Remove(izn);

                    SacuvajKnjigeUFajl();
                    SacuvajIznajmljivanjaUFajl();

                    Console.WriteLine($"\n[VRATI] Klijent {p.KlijentID} vratio knjigu: {knjiga.Naslov}");
                    s.Send(Encoding.UTF8.GetBytes("VRACENO_OK"));
                }
            }
        }

    

        private static void UcitajIznajmljivanjaIzFajla()
        {
            _iznajmljivanja.Clear();

            if (!File.Exists(IZN_FAJL))
                return;

            var lines = File.ReadAllLines(IZN_FAJL);
            foreach (var line in lines)
            {
                // format: ClanID|Naslov|Autor|DatumVracanja(dd.MM.yyyy)
                var p = line.Split('|');
                if (p.Length < 4) continue;

                if (!int.TryParse(p[0], out int clanId)) continue;

                _iznajmljivanja.Add(new Iznajmljivanje
                {
                    ClanID = clanId,
                    KnjigaInfo = $"{p[1]}|{p[2]}",
                    DatumVracanja = p[3]
                });
            }

            Console.WriteLine($"[BAZA] Učitano {_iznajmljivanja.Count} iznajmljivanja iz fajla.");
        }

        private static void SacuvajIznajmljivanjaUFajl()
        {
            List<string> lines = new List<string>();

            foreach (var izn in _iznajmljivanja)
            {
                // KnjigaInfo: "Naslov|Autor"
                var parts = izn.KnjigaInfo.Split('|');
                string naslov = parts.Length > 0 ? parts[0] : "";
                string autor = parts.Length > 1 ? parts[1] : "";

                // format: ClanID|Naslov|Autor|DatumVracanja
                lines.Add($"{izn.ClanID}|{naslov}|{autor}|{izn.DatumVracanja}");
            }

            File.WriteAllLines(IZN_FAJL, lines);
        }

        private static bool JeRokProsao(string datumVracanja)
        {
            if (!DateTime.TryParseExact(datumVracanja, "dd.MM.yyyy",
                    System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.None, out DateTime rok))
                return false; 

            return DateTime.Now.Date > rok.Date;
        }

        private static void PosaljiKasnjenjaKlijentuAkoPostoje(Socket klijent, int clientId)
        {
            var kasni = new List<Iznajmljivanje>();
            foreach (var izn in _iznajmljivanja)
            {
                if (izn.ClanID == clientId && JeRokProsao(izn.DatumVracanja))
                    kasni.Add(izn);
            }

            if (kasni.Count == 0) return;

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("OPOMENA: Imate knjige koje nisu vraćene na vreme!");
            foreach (var k in kasni)
            {
                var parts = k.KnjigaInfo.Split('|');
                string naslov = parts.Length > 0 ? parts[0] : "";
                string autor = parts.Length > 1 ? parts[1] : "";
                sb.AppendLine($"- {naslov} ({autor}) | Rok: {k.DatumVracanja}");
            }

            
            klijent.Send(Encoding.UTF8.GetBytes(sb.ToString()));
        }

        private static void IspisiKasnjenjaNaServeru()
        {
            int cnt = 0;
            foreach (var izn in _iznajmljivanja)
                if (JeRokProsao(izn.DatumVracanja))
                    cnt++;

            if (cnt > 0)
                Console.WriteLine($"[INFO] Postoji {cnt} iznajmljivanja kojima je rok prošao.");
        }

     
        private static void UcitajKnjigeIzFajla()
        {
            if (File.Exists(KNJIGE_FAJL))
            {
                string[] linije = File.ReadAllLines(KNJIGE_FAJL);
                _knjige.Clear();

                foreach (var l in linije)
                {
                    var delovi = l.Split('|');
                    if (delovi.Length == 3 && int.TryParse(delovi[2], out int kol))
                    {
                        _knjige.Add(new Knjiga
                        {
                            Naslov = delovi[0],
                            Autor = delovi[1],
                            Kolicina = kol
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
                linije.Add($"{k.Naslov}|{k.Autor}|{k.Kolicina}");

            File.WriteAllLines(KNJIGE_FAJL, linije);
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
            Console.Write("Naslov: ");
            string n = Console.ReadLine() ?? "";
            Console.Write("Autor: ");
            string a = Console.ReadLine() ?? "";
            Console.Write("Kolicina: ");
            int.TryParse(Console.ReadLine(), out int kol);

            _knjige.Add(new Knjiga { Naslov = n, Autor = a, Kolicina = kol });
            SacuvajKnjigeUFajl();

            Console.WriteLine("Knjiga uspesno dodata.");
        }

        private static void PrikaziSveKnjige()
        {
            Console.WriteLine("\n--- Stanje u biblioteci ---");
            if (_knjige.Count == 0)
                Console.WriteLine("Nema knjiga.");
            else
                foreach (var k in _knjige)
                    Console.WriteLine($"[{k.Kolicina}] {k.Naslov} - {k.Autor}");
        }
    }
}