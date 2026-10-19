namespace ManifestLE.Core.Models;

public enum StorageLocationType
{
    Permanent = 0,
    Temporary = 1
}

public class StorageLocation
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public Guid? ParentLocationId { get; set; }

    public StorageLocationType LocationType { get; set; }
        = StorageLocationType.Permanent;

    public bool IsActive { get; set; } = true;
}