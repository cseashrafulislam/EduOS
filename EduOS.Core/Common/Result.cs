namespace EduOS.Core.Common
{
    public class Result
    {
        public bool Succeeded { get; set; }
        public string? Message { get; set; }

        public static Result Success(string? message = null) => new Result
        {
            Succeeded = true,
            Message = message
        };

        public static Result Failure(string message) => new Result
        {
            Succeeded = false,
            Message = message
        };
    }
}