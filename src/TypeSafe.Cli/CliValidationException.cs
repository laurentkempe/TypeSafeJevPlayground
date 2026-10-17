namespace TypeSafe.Cli;

public sealed class CliValidationException(string message, Exception? inner = null) : Exception(message, inner);
