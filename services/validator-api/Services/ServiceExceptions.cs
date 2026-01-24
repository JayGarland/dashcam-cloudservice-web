using System;

namespace ValidatorApi.Services;

public class NotFoundException : Exception
{
    public NotFoundException(string message) : base(message)
    {
    }
}

public class SessionExpiredException : Exception
{
    public SessionExpiredException(string message) : base(message)
    {
    }
}
