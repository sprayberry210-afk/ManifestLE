namespace ManifestLE.Core.Models;

public class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? ParentLocationId { get; set; }

    public bool IsActive { get; set; } = true;
}