namespace ii.TotalAnnihilation.Model.Tdf;

public class TdfWeaponBlock
{
    public string SectionName { get; set; }
    public Dictionary<string, string> Properties { get; } = [];
    public TdfDamageBlock Damage { get; set; }
}
