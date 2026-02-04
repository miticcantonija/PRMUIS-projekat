using System;

namespace Biblioteka.Modeli
{
    [Serializable]
    public class Knjiga
    {
        public string Naslov { get; set; }
        public string Autor { get; set; }
        public int Kolicina { get; set; }

    }

    [Serializable]
    public class Poruka
    {
        public int KlijentID { get; set; }
        public Knjiga KnjigaPodaci { get; set; }
    }
}