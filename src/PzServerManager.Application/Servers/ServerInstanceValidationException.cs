namespace PzServerManager.Application.Servers;

public sealed class ServerInstanceValidationException(string message) : Exception(message);
