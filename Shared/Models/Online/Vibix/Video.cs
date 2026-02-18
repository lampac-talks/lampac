namespace Shared.Models.Online.Vibix
{
    public class Video
    {
        public string iframe_url { get; set; }

        public string type { get; set; }

        public string embed_code { get; set; }

        public Voiceover[] voiceovers { get; set; }
    }

    public class Voiceover
    {
        public int id { get; set; }
        public string name { get; set; }
    }
}
