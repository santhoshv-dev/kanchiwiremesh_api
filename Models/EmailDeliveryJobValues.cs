namespace KanchimeshAPI.Models;

public static class EmailDeliveryJobKinds
{
    public const string CustomerConfirmation = "CustomerConfirmation";
    public const string AdminNotification = "AdminNotification";
}

public static class EmailDeliveryJobStatuses
{
    public const string Pending = "Pending";
    public const string Processing = "Processing";
    public const string Sent = "Sent";
    public const string Failed = "Failed";
}
