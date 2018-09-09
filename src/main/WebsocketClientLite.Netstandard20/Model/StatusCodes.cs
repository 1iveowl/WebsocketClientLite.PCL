namespace WebsocketClientLite.PCL.Model
{
    internal enum StatusCodes
    {
        Normal = 1000,
        GoingAway = 1001,
        ProtocolError = 1002,
        UnacceptableReceiveType = 1003,
        InconsistentDataType = 1007,
        PolicyViolation = 1008,
        TooBig = 1009,
        MissingExtension = 1010,
        UnexpectedServerError = 1011,
    }
}
