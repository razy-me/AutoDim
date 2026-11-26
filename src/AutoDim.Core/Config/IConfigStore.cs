namespace AutoDim.Core.Config;

public interface IConfigStore
{
    string ConfigPath { get; }
    DimConfig Load();
    void Save(DimConfig config);
}
