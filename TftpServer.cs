using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

class TftpServer
{
    public void Listen(IPAddress remote, int port, string filePath, string mode)
    {
        int maxBlockSize = 512;
        byte[] buffer = new byte[maxBlockSize + 4];
        IPEndPoint? client = null;
        using UdpClient conn = new(69);
        conn.Client.ReceiveTimeout = 3000;

        string filename = Path.GetFileName(filePath);

        Console.CursorLeft = 0;
        Console.Write("                        ");
        Console.CursorLeft = 0;
        Console.Write($"sending: wrq");

        try
        {
            GetRequest(buffer, conn, ref client);
            ParseResult res = ParseResponse(buffer, conn, ref client);
            if (client == null || res.Op != OpCode.Wrq || res.Op != OpCode.Rrq)
            {
                SendError(buffer, conn, client, ErrorCode.Undefined, "bad request");
                return;
            }

            if (res.Op == OpCode.Wrq && res.Message != null)
            {
                GetFile(buffer, conn, ref client, res.Message);
            }
        }
        catch
        {
            Console.CursorLeft = 0;
            Console.WriteLine("tftpsharp: request failed");
            return;
        }
    }

    public void GetFile(byte[] buffer, UdpClient conn, ref IPEndPoint client, string fileName)
    {
        using FileStream file = File.OpenWrite(fileName);

        ushort currentBlock = 1;
        int retryCount = 2;
        int maxBlockSize = 512;
        int bytesRead = 0;
        int outputBlock = 1;
        int resends = 0;

        Console.CursorLeft = 0;
        Console.CursorVisible = false;
        Console.Write("sending: block #");

        Stopwatch timer = new();
        timer.Start();
        do
        {
            for (int i = 1; i <= retryCount; i++)
            {
                BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(), (ushort)OpCode.Data);
                BinaryPrimitives.WriteUInt16BigEndian(buffer.AsSpan(2), currentBlock);

                bytesRead = file.Read(buffer, 4, maxBlockSize);

                Console.CursorLeft = 16;
                Console.Write(outputBlock);
                conn.Send(buffer, 4 + bytesRead, client);

                ParseResult res = ParseResponse(buffer, conn, ref client!);

                if (res.Op == OpCode.Ack && res.BlockNumber == currentBlock)
                {
                    outputBlock++;
                    currentBlock++;
                    break;
                }

                if (res.Op == OpCode.Error || i >= retryCount)
                {
                    if (res.Op != OpCode.Error)
                    {
                        SendError(buffer, conn, client, ErrorCode.Undefined, "bad ack");
                    }
                    timer.Stop();

                    outputBlock--;

                    Console.CursorLeft = 0;
                    Console.Write("                        ");
                    Console.CursorLeft = 0;
                    Console.WriteLine("tftpsharp: transfer failed");
                    if (res.Op == OpCode.Error)
                    {
                        Console.WriteLine($"err received: {res.Message}");
                    }
                    Console.WriteLine(
                        $"  sent {outputBlock} blocks, {(512 * (outputBlock - 1) + bytesRead)} bytes, {resends} resends"
                    );
                    Console.WriteLine($"  elapsed time: {timer.ElapsedMilliseconds} ms");
                    Console.CursorVisible = true;
                    return;
                }

                resends++;
            }
        } while (bytesRead == maxBlockSize);

        timer.Stop();
        outputBlock--;

        Console.CursorLeft = 0;
        Console.Write("                        ");
        Console.CursorLeft = 0;
        Console.WriteLine("tftpsharp: transfer complete");
        Console.WriteLine(
            $"  sent {outputBlock} blocks, {(512 * (outputBlock - 1) + bytesRead)} bytes, {resends} resends"
        );
        Console.WriteLine($"  elapsed time: {timer.ElapsedMilliseconds} ms");
        Console.CursorVisible = true;

        return;
    }

    IPEndPoint GetRequest(byte[] buffer, UdpClient conn, ref IPEndPoint? endpoint)
    {
        buffer = conn.Receive(ref endpoint);
        return endpoint;
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

        Encoding.UTF8.GetBytes(errorMessage).CopyTo(buffer, 4);
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
                string errorMessage = Encoding.UTF8.GetString(buffer.AsSpan(4));
                return new ParseResult()
                {
                    Op = op,
                    Err = (ErrorCode)errorCode,
                    Message = errorMessage,
                };
            }
            case OpCode.Wrq:
                string filename = Encoding.UTF8.GetString(
                    buffer.AsSpan(2, Array.IndexOf(buffer, 0))
                );
                return new ParseResult() { Op = op, Message = filename };
            case OpCode.Rrq:
                filename = Encoding.UTF8.GetString(buffer.AsSpan(2, Array.IndexOf(buffer, 0)));
                return new ParseResult() { Op = op, Message = filename };
            default:
            {
                break;
            }
        }

        return new ParseResult() { Op = OpCode.Error };
    }
}
