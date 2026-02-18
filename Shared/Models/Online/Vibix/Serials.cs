namespace Shared.Models.Online.Vibix
{
    public class SerialsRoot
    {
        public SerialsSeason[] seasons { get; set; }
    }

    public class SerialsSeason
    {
        public string name { get; set; }

        public SerialsEpisode[] series { get; set; }
    }

    public class SerialsEpisode
    {
        public long id { get; set; }

        public string name { get; set; }
    }
}
