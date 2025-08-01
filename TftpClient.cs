using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

class TftpClient
{
    public void SendFile(IPAddress remote, int port, string filePath, string mode)
    {
        int maxBlockSize = 512;
        int retryCount = 2;
        byte[] buffer = new byte[maxBlockSize + 4];
        IPEndPoint tftpServer = new(remote, port);
        IPEndPoint? responder = null;
        using UdpClient conn = new();
        conn.Client.ReceiveTimeout = 3000;

        string filename = Path.GetFileName(filePath);

        Console.CursorLeft = 0;
        Console.Write("                        ");
        Console.CursorLeft = 0;
        Console.Write($"sending: wrq");

        try
        {
            SendRequest(buffer, conn, tftpServer, OpCode.Wrq, filename, mode);
            ParseResult res = ParseResponse(buffer, conn, ref responder);
            if (responder == null || res.Op != OpCode.Ack && res.BlockNumber != 0)
            {
                SendError(buffer, conn, responder, ErrorCode.Undefined, "bad ack");
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
                conn.Send(buffer, 4 + bytesRead, responder);

                ParseResult res = ParseResponse(buffer, conn, ref responder);

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
                        SendError(buffer, conn, responder, ErrorCode.Undefined, "bad ack");
                    }
                    timer.Stop();

                    outputBlock--;

                    Console.CursorLeft = 0;
                    Console.Write("                        ");
                    Console.CursorLeft = 0;
                    Console.WriteLine("tftpsharp: transfer failed");
                    if (res.Op == OpCode.Error)
                    {
                        Console.WriteLine($"err received: {res.ErrorMessage}");
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

        byte[] nameBytes = Encoding.UTF8.GetBytes(fileName);
        byte[] modeBytes = Encoding.UTF8.GetBytes(mode);

        nameBytes.AsSpan().CopyTo(buffer.AsSpan(2));
        modeBytes.AsSpan().CopyTo(buffer.AsSpan(2 + nameBytes.Length + 1));

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
                    ErrorMessage = errorMessage,
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

enum OpCode : ushort
{
    None,
    Rrq,
    Wrq,
    Data,
    Ack,
    Error,
}

enum ErrorCode : ushort
{
    Undefined,
    FileNotFound,
    AccessViolation,
    DiskFull,
    IllegalOp,
    UnknownId,
    FileExists,
    NoSuchUser,
}

class ParseResult
{
    public OpCode Op = OpCode.None;
    public ErrorCode? Err = null;
    public ushort? BlockNumber = null;
    public string? ErrorMessage = null;

    public ParseResult() { }
}
