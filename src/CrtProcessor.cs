using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ii.CompleteDestruction;

public class CrtProcessor
{
    private const uint ExpectedSignature = 0x3F800000;

    public CrtFile Read(string filePath)
    {
        using var br = new BinaryReader(File.Open(filePath, FileMode.Open));
        return Read(br);
    }

    public CrtFile Read(byte[] data)
    {
        using var ms = new MemoryStream(data);
        using var br = new BinaryReader(ms);
        return Read(br);
    }

    private CrtFile Read(BinaryReader br)
    {
        var crtFile = new CrtFile
        {
            Signature = br.ReadUInt32(),
            Unknown1 = br.ReadUInt32(),
            NumberOfUnits = br.ReadUInt32()
        };

        if (crtFile.Signature != ExpectedSignature)
        {
            throw new InvalidDataException($"Invalid CRT signature. Expected 0x{ExpectedSignature:X8}, got 0x{crtFile.Signature:X8}");
        }

        crtFile.Units = [];
        for (uint i = 0; i < crtFile.NumberOfUnits; i++)
        {
            var unit = new CrtUnit
            {
                UnitType = ReadNullTerminatedString(br, 256),
                UniqueName = ReadNullTerminatedString(br, 256),
                X = br.ReadUInt32(),
                Y = br.ReadUInt32(),
                Z = br.ReadUInt32(),
                PlayerId = br.ReadUInt32(),
                HealthPercent = br.ReadUInt32(),
                ArmorPercent = br.ReadUInt32(),
                WeaponPercent = br.ReadUInt32(),
                Angle = br.ReadUInt32(),
                Veteran = br.ReadUInt32(),
                Unknown1 = br.ReadUInt32(),
                Unknown2 = br.ReadUInt32(),
                FootprintX = br.ReadUInt32(),
                FootprintY = br.ReadUInt32(),
                Unknown3 = br.ReadUInt32()
            };
            crtFile.Units.Add(unit);
        }

        crtFile.NumberOfPlayers = br.ReadUInt32();
        crtFile.Players = [];
        for (uint i = 0; i < crtFile.NumberOfPlayers; i++)
        {
            var player = new CrtPlayer
            {
                NumberOfRules = br.ReadUInt32()
            };

            player.Rules = [];
            for (uint j = 0; j < player.NumberOfRules; j++)
            {
                var rule = new CrtRule
                {
                    NumberOfConditions = br.ReadUInt32()
                };

                // Conditions
                rule.Conditions = [];
                for (uint k = 0; k < rule.NumberOfConditions; k++)
                {
                    var condition = new CrtCondition
                    {
                        ConditionNumber = br.ReadUInt32()
                    };

                    // 5 arguments, 64 bytes each
                    condition.Arguments = [];
                    condition.ParsedArguments = [];
                    for (int argIdx = 0; argIdx < 5; argIdx++)
                    {
                        var argument = br.ReadBytes(64);
                        condition.Arguments.Add(argument);
                        condition.ParsedArguments.Add(ParseArgument(argument));
                    }

                    rule.Conditions.Add(condition);
                }

                // Actions
                rule.NumberOfActions = br.ReadUInt32();
                rule.Actions = [];
                for (uint k = 0; k < rule.NumberOfActions; k++)
                {
                    var action = new CrtAction
                    {
                        ActionNumber = br.ReadUInt32()
                    };

                    // 5 arguments, 64 bytes each
                    action.Arguments = [];
                    action.ParsedArguments = [];
                    for (int argIdx = 0; argIdx < 5; argIdx++)
                    {
                        var argument = br.ReadBytes(64);
                        action.Arguments.Add(argument);
                        action.ParsedArguments.Add(ParseArgument(argument));
                    }

                    rule.Actions.Add(action);
                }

                player.Rules.Add(rule);
            }

            crtFile.Players.Add(player);
        }

        // Triggers
        crtFile.NumberOfTriggers = br.ReadUInt32();
        crtFile.Triggers = [];
        for (uint i = 0; i < crtFile.NumberOfTriggers; i++)
        {
            var trigger = new CrtTrigger
            {
                TriggerName = ReadNullTerminatedString(br, 256),
                Left = br.ReadUInt32(),
                Top = br.ReadUInt32(),
                Right = br.ReadUInt32(),
                Bottom = br.ReadUInt32()
            };
            crtFile.Triggers.Add(trigger);
        }

        return crtFile;
    }

    public void Write(string filePath, CrtFile crtFile)
    {
        using var bw = new BinaryWriter(File.Open(filePath, FileMode.Create));
        Write(bw, crtFile);
    }

    public byte[] Write(CrtFile crtFile)
    {
        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);
        Write(bw, crtFile);
        return ms.ToArray();
    }

    private void Write(BinaryWriter bw, CrtFile crtFile)
    {
        bw.Write(crtFile.Signature);
        bw.Write(crtFile.Unknown1);
        bw.Write((uint)crtFile.Units.Count);

        foreach (var unit in crtFile.Units)
        {
            WriteNullTerminatedString(bw, unit.UnitType, 256);
            WriteNullTerminatedString(bw, unit.UniqueName, 256);
            bw.Write(unit.X);
            bw.Write(unit.Y);
            bw.Write(unit.Z);
            bw.Write(unit.PlayerId);
            bw.Write(unit.HealthPercent);
            bw.Write(unit.ArmorPercent);
            bw.Write(unit.WeaponPercent);
            bw.Write(unit.Angle);
            bw.Write(unit.Veteran);
            bw.Write(unit.Unknown1);
            bw.Write(unit.Unknown2);
            bw.Write(unit.FootprintX);
            bw.Write(unit.FootprintY);
            bw.Write(unit.Unknown3);
        }

        bw.Write((uint)crtFile.Players.Count);
        foreach (var player in crtFile.Players)
        {
            bw.Write((uint)player.Rules.Count);

            foreach (var rule in player.Rules)
            {
                bw.Write((uint)rule.Conditions.Count);

                foreach (var condition in rule.Conditions)
                {
                    bw.Write(condition.ConditionNumber);

                    for (int argIdx = 0; argIdx < 5; argIdx++)
                    {
                        if (argIdx < condition.Arguments.Count)
                        {
                            WriteFixedByteArray(bw, condition.Arguments[argIdx], 64);
                        }
                        else
                        {
                            bw.Write(new byte[64]);
                        }
                    }
                }

                bw.Write((uint)rule.Actions.Count);

                foreach (var action in rule.Actions)
                {
                    bw.Write(action.ActionNumber);

                    for (int argIdx = 0; argIdx < 5; argIdx++)
                    {
                        if (argIdx < action.Arguments.Count)
                        {
                            WriteFixedByteArray(bw, action.Arguments[argIdx], 64);
                        }
                        else
                        {
                            bw.Write(new byte[64]);
                        }
                    }
                }
            }
        }

        bw.Write((uint)crtFile.Triggers.Count);
        foreach (var trigger in crtFile.Triggers)
        {
            WriteNullTerminatedString(bw, trigger.TriggerName, 256);
            bw.Write(trigger.Left);
            bw.Write(trigger.Top);
            bw.Write(trigger.Right);
            bw.Write(trigger.Bottom);
        }
    }

    public string ToJson(CrtFile crtFile)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            Converters = { new ByteArrayConverter() }
        };

        return JsonSerializer.Serialize(crtFile, options);
    }

    private static string ReadNullTerminatedString(BinaryReader br, int maxLength)
    {
        var bytes = br.ReadBytes(maxLength);
        var nullIndex = Array.IndexOf(bytes, (byte)0);
        if (nullIndex >= 0)
        {
            return Encoding.ASCII.GetString(bytes, 0, nullIndex);
        }
        return Encoding.ASCII.GetString(bytes);
    }

    private static void WriteNullTerminatedString(BinaryWriter bw, string str, int fixedLength)
    {
        var bytes = new byte[fixedLength];
        if (!string.IsNullOrEmpty(str))
        {
            var strBytes = Encoding.ASCII.GetBytes(str);
            var length = Math.Min(strBytes.Length, fixedLength - 1);
            Array.Copy(strBytes, bytes, length);
        }
        bw.Write(bytes);
    }

    private static void WriteFixedByteArray(BinaryWriter bw, byte[] data, int fixedLength)
    {
        if (data.Length == fixedLength)
        {
            bw.Write(data);
        }
        else
        {
            var bytes = new byte[fixedLength];
            Array.Copy(data, bytes, Math.Min(data.Length, fixedLength));
            bw.Write(bytes);
        }
    }

    private static string ParseArgument(byte[] argument)
    {
        // All zeros = unused
        if (argument.All(b => b == 0))
        {
            return string.Empty;
        }

        // Decimal first
        var numericResult = TryParseDecimalEncoded(argument, out var value);
        if (numericResult)
        {
            return value.ToString();
        }

        // String
        var nullIndex = Array.IndexOf(argument, (byte)0);
        if (nullIndex == 0)
        {
            return string.Empty;
        }

        var length = nullIndex >= 0 ? nullIndex : argument.Length;
        
        // Printable ASCII
        var isPrintable = true;
        for (int i = 0; i < length; i++)
        {
            if (argument[i] < 32 || argument[i] > 126)
            {
                isPrintable = false;
                break;
            }
        }

        if (isPrintable && length > 0)
        {
            return Encoding.ASCII.GetString(argument, 0, length);
        }

        // Fallback to hex
        return "0x" + Convert.ToHexString(argument);
    }

    private static bool TryParseDecimalEncoded(byte[] data, out long value)
    {
        value = 0;
        var isNegative = false;
        var index = 0;

        // Check for negative sign
        if (data[index] == 0x2D)
        {
            isNegative = true;
            index++;
        }

        var hasDigits = false;
        // Parse decimal digits
        while (index < data.Length && data[index] != 0)
        {
            var b = data[index];
            
            // Check for 0x30 flag (digit marker)
            if ((b & 0x30) == 0x30)
            {
                int digit = b & 0x0F; // Extract the digit value
                if (digit >= 0 && digit <= 9)
                {
                    value = value * 10 + digit;
                    hasDigits = true;
                    index++;
                }
                else
                {
                    break;
                }
            }
            else
            {
                break;
            }
        }

        if (isNegative)
        {
            value = -value;
        }

        return hasDigits;
    }

    private class ByteArrayConverter : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            throw new NotImplementedException();
        }

        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options)
        {
            if (value == null || value.Length == 0 || value.All(b => b == 0))
            {
                writer.WriteStringValue(string.Empty);
            }
            else
            {
                writer.WriteStringValue("0x" + BitConverter.ToString(value).Replace("-", ""));
            }
        }
    }
}