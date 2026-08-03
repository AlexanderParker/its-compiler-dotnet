namespace InstructionTemplateSpecification;

/// <summary>Base exception for all ITS compilation failures.</summary>
public class ItsCompilationException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsCompilationException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the cause it wraps.</summary>
    public ItsCompilationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Template structure or input validation failure.</summary>
public class ItsValidationException : ItsCompilationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsValidationException(string message) : base(message) { }
}

/// <summary>Variable resolution or validation failure.</summary>
public class ItsVariableException : ItsCompilationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsVariableException(string message) : base(message) { }
}

/// <summary>Conditional expression failure.</summary>
public class ItsConditionalException : ItsCompilationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsConditionalException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the cause it wraps.</summary>
    public ItsConditionalException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Security policy violation (URL, protocol, size or content limits).</summary>
public class ItsSecurityException : ItsCompilationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsSecurityException(string message) : base(message) { }
}

/// <summary>Schema loading failure.</summary>
public class ItsSchemaException : ItsCompilationException
{
    /// <summary>Creates the exception with a message.</summary>
    public ItsSchemaException(string message) : base(message) { }

    /// <summary>Creates the exception with a message and the cause it wraps.</summary>
    public ItsSchemaException(string message, Exception inner) : base(message, inner) { }
}
