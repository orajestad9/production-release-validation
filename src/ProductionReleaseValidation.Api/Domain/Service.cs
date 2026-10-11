namespace ProductionReleaseValidation.Api.Domain
{
    public class Service
    {
        public Guid Id { get; private set; } = Guid.CreateVersion7();
        public string Name { get; }
        public DateTimeOffset CreatedAtUtc { get; }

        public Service(string name, DateTimeOffset createdAtUtc)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            Name = name;
            CreatedAtUtc = createdAtUtc;
        }
    }
}
