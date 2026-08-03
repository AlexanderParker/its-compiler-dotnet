namespace InstructionTemplateSpecification;

/// <summary>Base exception for all ITS compilation failures.</summary>
public class ItsCompilationException : Exception
{
    public ItsCompilationException(string message) : base(message) { }
    public ItsCompilationException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>Template structure or input validation failure.</summary>
public class ItsValidationException : ItsCompilationException
{
    public ItsValidationException(string message) : base(message) { }
}

/// <summary>Variable resolution or validation failure.</summary>
public class ItsVariableException : ItsCompilationException
{
    public ItsVariableException(string message) : base(message) { }
}

/// <summary>Conditional expression failure.</summary>
public class ItsConditionalException : ItsCompilationException
{
    public ItsConditionalException(string message) : base(message) { }
}

/// <summary>Security policy violation (URL, protocol, size or content limits).</summary>
public class ItsSecurityException : ItsCompilationException
{
    public ItsSecurityException(string message) : base(message) { }
}

/// <summary>Schema loading failure.</summary>
public class ItsSchemaException : ItsCompilationException
{
    public ItsSchemaException(string message) : base(message) { }
    public ItsSchemaException(string message, Exception inner) : base(message, inner) { }
}
