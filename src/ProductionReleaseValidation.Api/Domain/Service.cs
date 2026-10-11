namespace ProductionReleaseValidation.Api.Domain
{
    public class Service
    {
        public string Name { get;}
        public DateTimeOffset CreatedAtUtc { get;}

        public Service(string name, DateTimeOffset createdAtUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);
            
            Name = name;
            CreatedAtUtc = createdAtUtc;
        }
    }
}