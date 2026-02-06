#pragma warning disable SYSLIB0011
using System;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Text;
using System.Runtime.Serialization.Formatters.Binary;
using Biblioteka.Modeli; // Ovde se nalaze Knjiga i Poruka

namespace BibliotekaKlijent
{
    public class Program
    {
        static void Main(string[] args)
        {
            // 1. Inicijalizacija TCP i UDP soketa
            Socket klijentTcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            Socket klijentUdp = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);

            IPEndPoint serverTcpEP = new IPEndPoint(IPAddress.Loopback, 6000);
            IPEndPoint serverUdpEP = new IPEndPoint(IPAddress.Loopback, 5000);

            try
            {
                // 2. TCP Povezivanje i prijem ID-a
                Console.WriteLine("Povezujem se na server...");
                klijentTcp.Connect(serverTcpEP);

                byte[] idBuf = new byte[256];
                int primljeno = klijentTcp.Receive(idBuf);
                int mojID = int.Parse(Encoding.UTF8.GetString(idBuf, 0, primljeno));

                Console.WriteLine($"[USPEH] Prijavljen na server! Dodeljen ID: {mojID}");

                while (true)
                {
                    Console.WriteLine("\n========== KLIJENT MENI ==========");
                    Console.WriteLine("1 - Pošalji novu knjigu (TCP)");
                    Console.WriteLine("2 - Proveri dostupnost (UDP)");
                    Console.WriteLine("3 - Preuzmi listu svih knjiga (UDP)");
                    Console.WriteLine("4 - IZNAJMI KNJIGU (TCP)"); // NOVA OPCIJA
                    Console.WriteLine("5 - Pogledaj moje iznajmljene knjige (Lokalno)"); // NOVA OPCIJA
                    Console.WriteLine("0 - Izlaz");
                    Console.Write("Izbor: ");
                    string izbor = Console.ReadLine();

                    if (izbor == "1")
                    {
                        // SLANJE KNJIGE PREKO TCP-A
                        Knjiga k = new Knjiga();
                        Console.Write("Naslov: "); k.Naslov = Console.ReadLine();
                        Console.Write("Autor: "); k.Autor = Console.ReadLine();
                        Console.Write("Količina: ");
                        k.Kolicina = int.TryParse(Console.ReadLine(), out int kol) ? kol : 1;

                        Poruka p = new Poruka { KlijentID = mojID, KnjigaPodaci = k };

                        using (MemoryStream ms = new MemoryStream())
                        {
                            BinaryFormatter bf = new BinaryFormatter();
                            bf.Serialize(ms, p);
                            klijentTcp.Send(ms.ToArray());
                        }
                        Console.WriteLine("[TCP] Knjiga poslata sa tvojim ID-em.");
                    }
                    else if (izbor == "2")
                    {
                        // UPIT ZA KNJIGU PREKO UDP-A
                        Console.Write("Unesite naslov za proveru: ");
                        string naslov = Console.ReadLine();
                        byte[] data = Encoding.UTF8.GetBytes("PROVERA:" + naslov);
                        klijentUdp.SendTo(data, serverUdpEP);

                        // Prijem odgovora
                        byte[] buffer = new byte[2048];
                        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        int n = klijentUdp.ReceiveFrom(buffer, ref remote);
                        Console.WriteLine("\n[UDP ODGOVOR]: " + Encoding.UTF8.GetString(buffer, 0, n));
                    }
                    else if (izbor == "3")
                    {
                        // UPIT ZA LISTU PREKO UDP-A
                        byte[] data = Encoding.UTF8.GetBytes("LISTA:SVE");
                        klijentUdp.SendTo(data, serverUdpEP);

                        // Prijem odgovora
                        byte[] buffer = new byte[4096];
                        EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
                        int n = klijentUdp.ReceiveFrom(buffer, ref remote);
                        Console.WriteLine("\n[LISTA KNJIGA]:\n" + Encoding.UTF8.GetString(buffer, 0, n));
                    }
                    else if (izbor == "4") // IZNAJMLJIVANJE
                    {
                        Console.Write("Unesite tačan naslov knjige koju želite da iznajmite: ");
                        string naslov = Console.ReadLine();

                        // Pakujemo poruku sa tipom "IZNAJMI"
                        Poruka p = new Poruka
                        {
                            KlijentID = mojID,
                            KnjigaPodaci = new Knjiga { Naslov = naslov },
                            TipPoruke = "IZNAJMI"
                        };

                        // 1. Slanje zahteva serveru
                        using (MemoryStream ms = new MemoryStream())
                        {
                            BinaryFormatter bf = new BinaryFormatter();
                            bf.Serialize(ms, p);
                            klijentTcp.Send(ms.ToArray());
                        }

                        // 2. Čekanje potvrde od servera (TCP Receive)
                        byte[] potvrdaBuf = new byte[1024];
                        int n = klijentTcp.Receive(potvrdaBuf);
                        string odgovor = Encoding.UTF8.GetString(potvrdaBuf, 0, n);

                        if (odgovor.StartsWith("USPESNO"))
                        {
                            string datum = odgovor.Split('|')[1];
                            Console.WriteLine($"\n[USPEH] Knjiga je iznajmljena! Rok za vraćanje: {datum}");

                            // 3. Čuvanje u lokalni fajl (da ostane i kad ugasiš program)
                            string zapis = $"Knjiga: {naslov} | Vratiti do: {datum} | Datum uzimanja: {DateTime.Now:dd.MM.yyyy}";
                            File.AppendAllLines("iznajmljeno.txt", new[] { zapis });
                            Console.WriteLine("Zapis sačuvan u lokalnu evidenciju (iznajmljeno.txt).");
                        }
                        else
                        {
                            Console.WriteLine("\n[GREŠKA] Server je odbio zahtev. Knjiga možda nije na stanju.");
                        }
                    }
                    else if (izbor == "5") // PREGLED LOKALNOG FAJLA
                    {
                        Console.WriteLine("\n--- MOJA LOKALNA EVIDENCIJA ---");
                        if (File.Exists("iznajmljeno.txt"))
                        {
                            string[] zapisi = File.ReadAllLines("iznajmljeno.txt");
                            foreach (var z in zapisi) Console.WriteLine(z);
                        }
                        else
                        {
                            Console.WriteLine("Nemate sačuvanih iznajmljivanja.");
                        }
                    }
                    else if (izbor == "0")
                    {
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("\n[GREŠKA]: " + ex.Message);
            }
            finally
            {
                klijentTcp.Close();
                klijentUdp.Close();
                Console.WriteLine("Konekcije zatvorene. Pritisnite bilo koji taster za kraj.");
                Console.ReadKey();
            }
        }
    }
}