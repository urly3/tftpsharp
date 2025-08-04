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
    public string? Message = null;

    public ParseResult() { }
}

