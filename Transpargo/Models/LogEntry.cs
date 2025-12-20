namespace Transpargo.Models
{
    public class LogEntry
    {
        public string date { get; set; } = null;
        public string time { get; set; } = null;
        public string icon { get; set; }
        public string agent { get; set; }
        public string title { get; set; }
        public bool action { get; set; } = false;
        public string actionLabel { get; set; } = null;
        public string action_href { get; set; } = null;
    }
}
