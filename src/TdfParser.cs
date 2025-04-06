using ii.TotalAnnihilation.Model.Tdf;
using System.Text.RegularExpressions;

namespace ii.TotalAnnihilation.Model;

public class TdfParser
{
    //TODO: Do we care about case-sensitivity
    //TODO: Do we want a soft-coded list of permissable properties (and indicate the type)

    public TdfWeaponDefinition Parse(string filePath)
    {
        var fileContent = File.ReadAllText(filePath);


        // Strip out multi-line comments
        fileContent = Regex.Replace(fileContent, "/\\*.*?\\*/", " ", RegexOptions.Singleline);
        var lines = fileContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(line => line.Trim())
                               .ToArray();


        // Strip out single-line comments
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.IndexOf("//") > -1)
            {
                lines[i] = line.Substring(0, line.IndexOf("//"));
            }
        }


        // Parse weapon blocks
        var result = new TdfWeaponDefinition();
        var index = 0;
        while (index < lines.Length)
        {
            var line = lines[index].Trim();
            if (string.IsNullOrWhiteSpace(line))
            {
                index++;
                continue;
            }

            if (line.StartsWith('['))
            {
                var weapon = ParseWeaponBlock(lines, ref index);
                result.Weapons.Add(weapon);
                ValidateWeapon(weapon, result);
            }
            else
            {
                // File is invalid if we hit this?
                index++;
            }
        }

        return result;
    }

    private TdfWeaponBlock ParseWeaponBlock(string[] lines, ref int index)
    {
        var weapon = new TdfWeaponBlock
        {
            SectionName = lines[index++].Trim().Trim('[', ']')
        };

        while (index < lines.Length)
        {
            var line = lines[index].Trim();
            if (line == "}")
            {
                index++;
                break;
            }

            if (line.StartsWith("[DAMAGE]"))
            {
                index++;
                weapon.Damage = ParseDamageBlock(lines, ref index);
            }
            else if (line.Contains('='))
            {
                var parts = line.Split('=', 2);
                var key = parts[0].Trim();
                if (!parts[1].EndsWith(';'))
                {
                    //TODO: Validation error
                }
                var value = parts[1].Trim().TrimEnd(';');
                weapon.Properties[key] = value;
                index++;
            }
            else
            {
                index++;
            }
        }

        return weapon;
    }

    private TdfDamageBlock ParseDamageBlock(string[] lines, ref int index)
    {
        var damage = new TdfDamageBlock();

        while (index < lines.Length)
        {
            var line = lines[index].Trim();
            if (line == "}")
            {
                index++;
                break;
            }

            if (line.Contains('='))
            {
                var parts = line.Split(new[] { '=' }, 2);
                var key = parts[0].Trim();
                if (!parts[1].EndsWith(';'))
                {
                    //TODO: Validation error
                }
                var value = parts[1].Trim().TrimEnd(';');
                damage.Properties[key] = value;
            }

            index++;
        }

        return damage;
    }

    private void ValidateWeapon(TdfWeaponBlock weapon, TdfWeaponDefinition result)
    {
        if (!weapon.Properties.ContainsKey("ID"))
            result.Errors.Add($"{weapon.SectionName}: Missing required ID property");

        if (!weapon.Properties.ContainsKey("name"))
            result.Errors.Add($"{weapon.SectionName}: Missing required name property");

        if (weapon.Damage == null || !weapon.Damage.Properties.ContainsKey("default"))
            result.Errors.Add($"{weapon.SectionName}: Missing required DAMAGE section");
    }
}