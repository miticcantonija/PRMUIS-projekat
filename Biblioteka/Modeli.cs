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
        public string TipPoruke { get; set; }
    }

    [Serializable]
    public class Iznajmljivanje
    {
        public string KnjigaInfo { get; set; } // Naslov i Autor
        public int ClanID { get; set; }
        public string DatumVracanja { get; set; }
    }
}