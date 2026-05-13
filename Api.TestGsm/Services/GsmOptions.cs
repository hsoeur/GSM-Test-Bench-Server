namespace Api.TestGsm.Services;

public sealed class GsmOptions
{
 public string? PortName { get; set; }
 public int? BaudRate { get; set; }
 public int? DataBits { get; set; }
 public string? StopBits { get; set; } 
 public string? Parity { get; set; } 
 public string? Handshake { get; set; } 
 public string? NewLine { get; set; } 
 public int? ReadTimeout { get; set; }
 public int? WriteTimeout { get; set; }
}
