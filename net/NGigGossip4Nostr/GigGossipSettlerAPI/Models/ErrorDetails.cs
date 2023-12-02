using System.Text.Json;

namespace GigGossipSettlerAPI.Models
{
    public class ErrorDetails
    {
        public string Message { get; set; }
        public int ApiErrorCode { get; set; }

        public override string ToString()
        {
            return JsonSerializer.Serialize(this);
        }
    }
}
