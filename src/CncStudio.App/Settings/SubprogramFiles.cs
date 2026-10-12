namespace CncStudio.App.Settings;

/// <summary>
/// Procura subprogramas externos na pasta do programa aberto, sem diferenciar maiúsculas:
/// Fanuc "1234" → O1234, O1234.nc, 1234.nc, O1234.txt...; Siemens "L10"/"FURO" → L10.SPF, FURO.SPF, FURO.MPF...
/// </summary>
public static class SubprogramFiles
{
    private static readonly string[] Extensions = ["", ".nc", ".spf", ".mpf", ".txt", ".tap", ".cnc", ".iso", ".min", ".eia"];

    public static string? Load(string folder, string name)
    {
        try
        {
            if (!Directory.Exists(folder))
            {
                return null;
            }

            var candidates = new List<string> { name };
            if (int.TryParse(name, out int number))
            {
                candidates.Add("O" + number.ToString("0000"));
                candidates.Add("O" + number);
                candidates.Add(number.ToString("0000"));
            }

            // Nomes de arquivo aceitos: <nome><extensão> e o formato Siemens "<NOME>_SPF"
            var accepted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string candidate in candidates)
            {
                foreach (string extension in Extensions)
                {
                    accepted.Add(candidate + extension);
                }

                accepted.Add(candidate + "_SPF");
                accepted.Add(candidate + "_MPF");
            }

            foreach (string file in Directory.EnumerateFiles(folder))
            {
                if (accepted.Contains(Path.GetFileName(file)))
                {
                    return File.ReadAllText(file);
                }
            }
        }
        catch (Exception)
        {
            // Pasta inacessível: trata como "não encontrado"
        }

        return null;
    }
}
