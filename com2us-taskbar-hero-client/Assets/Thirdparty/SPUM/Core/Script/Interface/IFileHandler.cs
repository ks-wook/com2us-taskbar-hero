public interface IFileHandler
{
    public SPUM_Prefabs Save(SPUM_Prefabs prefabs, SPUM_Manager manager);
    public SPUM_Prefabs Edit(SPUM_Prefabs prefabs, SPUM_Manager manager);
    public SPUM_Prefabs[] Load();
    public void Delete(SPUM_Prefabs prefabs);
}

