using System.Net;

TftpClient client = new();

// tftpsharp 127.0.0.1 69 test.txt

if (args.Length == 1)
{
    if (args[0] == "-h" || args[0] == "--help")
    {
        Console.WriteLine(
            """
            tftpsharp: simple tftp client in c#

            note:
            always uses octet mode
            will retry once after a 3sec timout

            tftpsharp host_ip host_port filepath
              - host_ip: ipv4 address
              - host_port: udp port, usually 69
              - filepath: relative or absolute file path
            """
        );
        return 1;
    }

    Console.WriteLine("tftpsharp: unknown arg");
    return 1;
}
if (args.Length != 3)
{
    Console.WriteLine(
        """
        tftpsharp host_ip host_port filepath
        """
    );
    return 1;
}

if (!IPAddress.TryParse(args[0], out IPAddress? host))
{
    Console.WriteLine("tftpsharp: invalid host_ip");
    return 1;
}

if (!ushort.TryParse(args[1], out ushort host_port))
{
    Console.WriteLine("tftpsharp: invalid host_port");
    return 1;
}

string filePath = args[2];

if (!File.Exists(filePath))
{
    Console.WriteLine("tftpsharp: file does not exist");
    return 1;
}

client.SendFile(host, host_port, filePath, "octet");

return 0;
