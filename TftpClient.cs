using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using static System.Text.Encoding;

class TftpClient
{
    Queue<byte[]> PacketQueue = [];

    public void SendFile(IPAddress remote, int port, string filePath, string mode)
    {
        int maxBlockSize = 512;
        int retryCount = 2;
        byte[] buffer = new byte[maxBlockSize + 4];
        IPEndPoint tftpServer = new(remote, port);
        IPEndPoint? peer = null;
        using UdpClient conn = new();
        conn.Client.ReceiveTimeout = 3000;

        string filename = Path.GetFileName(filePath);

        Console.CursorLeft = 0;
        // Console.Write("                        ");
        Console.CursorLeft = 0;
        // Console.Write($"sending: wrq");

        try
        {
            SendRequest(buffer, conn, tftpServer, OpCode.Wrq, filename, mode);
            ParseResult res = ParseResponse(buffer, conn, ref peer);
            if (peer == null || res.Op != OpCode.Ack && res.BlockNumber != 0)
            {
                SendError(buffer, conn, peer, ErrorCode.Undefined, "bad ack");
                return;
            }
        }
        catch
        {
            Console.CursorLeft = 0;
            Console.WriteLine("tftpsharp: request failed");
            return;
        }

        using FileStream file = File.OpenRead(filePath);

        ushort currentBlock = 1;
        int bytesRead = 512;
        int resends = 0;

        Console.CursorLeft = 0;
        Console.CursorVisible = false;
        // Console.Write("sending: block #");

        Stopwatch timer = new();
        timer.Start();
        do
        {
            if (PacketQueue.Count == 0)
            {
                for (int i = 0; i < 20 && bytesRead == 512; i++)
                {
                    byte[] arr = new byte[512];
                    bytesRead = file.Read(arr);
                    PacketQueue.Enqueue(arr);
                }
            }

            BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)OpCode.Data);
            BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), currentBlock);

            byte[] block = PacketQueue.Dequeue();
            block.CopyTo(buffer, 4);

            for (int attempt = 1; attempt <= retryCount; attempt++)
            {
                conn.Send(buffer, 4 + block.Length, peer);

                ParseResult res = ParseResponse(buffer, conn, ref peer);

                if (res.Op == OpCode.Ack && res.BlockNumber == currentBlock)
                {
                    currentBlock++;
                    break;
                }

                if (res.Op == OpCode.Error || attempt >= retryCount)
                {
                    if (res.Op != OpCode.Error)
                    {
                        SendError(buffer, conn, peer, ErrorCode.Undefined, "bad ack");
                    }
                    timer.Stop();

                    Console.CursorLeft = 0;
                    Console.Write("                        ");
                    Console.CursorLeft = 0;
                    Console.WriteLine("tftpsharp: transfer failed");
                    if (res.Op == OpCode.Error)
                    {
                        Console.WriteLine($"err received: {res.Message}");
                    }
                    Console.WriteLine(
                        $"  sent {currentBlock - 1} blocks, {(512 * (currentBlock - 2) + block.Length)} bytes, {resends} resends"
                    );
                    Console.WriteLine($"  elapsed time: {timer.ElapsedMilliseconds} ms");
                    Console.CursorVisible = true;
                    return;
                }

                resends++;
            }
        } while (bytesRead == maxBlockSize);

        timer.Stop();

        Console.CursorLeft = 0;
        Console.Write("                        ");
        Console.CursorLeft = 0;
        Console.WriteLine("tftpsharp: transfer complete");
        Console.WriteLine(
            $"  sent {currentBlock - 1} blocks, {(512 * (currentBlock - 2) + bytesRead)} bytes, {resends} resends"
        );
        Console.WriteLine($"  elapsed time: {timer.ElapsedMilliseconds} ms");
        Console.CursorVisible = true;

        return;
    }

    void SendRequest(
        byte[] buffer,
        UdpClient conn,
        IPEndPoint endpoint,
        OpCode type,
        string fileName,
        string mode
    )
    {
        int totalLength = 4 + fileName.Length + mode.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)OpCode.Wrq);

        byte[] nameBytes = UTF8.GetBytes(fileName);
        byte[] modeBytes = UTF8.GetBytes(mode);

        nameBytes.CopyTo(buffer, 2);
        modeBytes.CopyTo(buffer, 2 + nameBytes.Length + 1);

        conn.Send(buffer, totalLength, endpoint);
    }

    void SendError(
        byte[] buffer,
        UdpClient conn,
        IPEndPoint? endpoint,
        ErrorCode errCode,
        string errorMessage
    )
    {
        int totalLength = 4 + errorMessage.Length;
        BinaryPrimitives.WriteUInt16BigEndian(buffer, (ushort)OpCode.Error);
        BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), (ushort)errCode);

        UTF8.GetBytes(errorMessage).CopyTo(buffer, 4);
        buffer[totalLength] = 0;

        conn.Send(buffer, totalLength + 1, endpoint);
    }

    ParseResult ParseResponse(byte[] buffer, UdpClient conn, ref IPEndPoint? endpoint)
    {
        try
        {
            buffer = conn.Receive(ref endpoint);
        }
        catch
        {
            return new ParseResult() { Op = OpCode.None };
        }

        ushort recvOp = BinaryPrimitives.ReadUInt16BigEndian(buffer);
        OpCode op = (OpCode)recvOp;

        switch (op)
        {
            case OpCode.Ack:
            {
                ushort blockNum = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
                return new ParseResult() { Op = op, BlockNumber = blockNum };
            }
            case OpCode.Error:
            {
                ushort errorCode = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(2));
                string errorMessage = UTF8.GetString(buffer, 4, buffer.Length);
                return new ParseResult()
                {
                    Op = op,
                    Err = (ErrorCode)errorCode,
                    Message = errorMessage,
                };
            }
            default:
            {
                break;
            }
        }

        return new ParseResult() { Op = OpCode.Error };
    }
}
