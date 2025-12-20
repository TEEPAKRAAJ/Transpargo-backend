namespace Transpargo.DTOs
{
    public class ChangeHsCodeRequest
    {
        public string hs { get; set; }        // final / approved HS
        public string senderHs { get; set; }  // sender-provided HS
    }
}
