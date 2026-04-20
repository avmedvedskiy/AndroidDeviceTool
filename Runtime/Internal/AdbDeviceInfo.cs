using System;

internal readonly struct AdbDeviceInfo
{
    public readonly string Id;
    public readonly string Model;
    public readonly string Product;
    public readonly string Device;

    public AdbDeviceInfo(string id, string model, string product, string device)
    {
        Id = id;
        Model = model;
        Product = product;
        Device = device;
    }

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(Model))
                return Model + " (" + Id + ")";

            if (!string.IsNullOrWhiteSpace(Device))
                return Device + " (" + Id + ")";

            if (!string.IsNullOrWhiteSpace(Product))
                return Product + " (" + Id + ")";

            return "Device (" + Id + ")";
        }
    }
}
