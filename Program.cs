using System.Net;

TftpClient client = new();

client.SendFile(IPAddress.Parse("127.0.0.1"), 69, "test.txt", "octet");
