using System.Net;
using System.Net.Sockets;

// info:
// string is netascii with a null termination (the 0byte)
//
// rrq/wrq packets:
// 2bytes opcode (1, 2) | filename string | 0byte | mode string | 0byte
// modes:
// netascii, octet, mail (deprecated)
//
// data packets:
// 2bytes opcode (3) | 2bytes block# | nbytes data
// 512 n indicates there are more blocks to come, <=511 == EOF
// i.e. packet < 516
//
// ack packet:
// 2bytes opcode (4) | 2bytes block#
//
// error packet:
// 2bytes opcode (5) | 2bytes errorcode | string err | 0byte

class TftpClient
{
    public void SendFile(IPAddress remote, int port, string filepath, string mode)
    {
        IPEndPoint tftpServer = new(remote, 69);
        // start the connection to specified host and port
        UdpClient connection = new();

        // send a wrq packet to initiate the connection
        byte[] buffer = new byte[516];

        byte[] opcode = BitConverter.GetBytes((ushort)2);

        buffer[0] = opcode[1];
        buffer[1] = opcode[0];

        int cur = 2;

        for (int i = 0; i < filepath.Length; i++, cur++)
        {
            buffer[cur] = (byte)filepath[i];
        }

        buffer[cur] = 0;
        cur++;

        for (int i = 0; i < mode.Length; i++, cur++)
        {
            buffer[cur] = (byte)mode[i];
        }

        buffer[cur] = 0;
        cur++;

        connection.Send(buffer, 4 + filepath.Length + mode.Length, tftpServer);

        // (wait to)receive the ack packet, and send data in a loop
        // start from block 1, sending the data;
        // re-send if no ack, retry a few times
        // TODO: timeouts and re-sending
        IPEndPoint? remoteEndpoint = null;

        using FileStream file = File.OpenRead(filepath);
        int read = 0;
        ushort currentBlock = 0;

        do
        {
            byte[] ack = connection.Receive(ref remoteEndpoint);
            Array.Reverse(ack);
            if (BitConverter.ToUInt16(ack) != currentBlock)
            {
                Console.WriteLine(
                    $"got ack for block {BitConverter.ToUInt16(ack)}, wanted {currentBlock}"
                );
                throw new Exception("incorrect block acknowledged");
            }
            currentBlock++;

            // set up data packet
            opcode = BitConverter.GetBytes((ushort)3);
            buffer[0] = opcode[1];
            buffer[1] = opcode[0];

            byte[] blockNumBytes = BitConverter.GetBytes((ushort)currentBlock);
            buffer[2] = blockNumBytes[1];
            buffer[3] = blockNumBytes[0];

            read = file.Read(buffer, 4, 512);

            connection.Send(buffer, 4 + read, remoteEndpoint);
        } while (read > 0);

        return;
    }
}
