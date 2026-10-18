using CncStudio.Core.Geometry;

namespace CncStudio.Core.Scene;

/// <summary>Vistas prontas da cena.</summary>
public enum SceneView
{
    Isometric,
    Top,
    Front,
    Side,
}

/// <summary>
/// Câmera em órbita em volta de um alvo, com Z para cima (a mesma convenção de
/// <see cref="Rendering.ViewCamera.Orbit"/>): azimute em graus a partir de −Y (frente) girando
/// para +X; elevação em graus acima do plano XY. Os motores 3D só leem o resultado.
/// </summary>
public sealed record SceneCamera(Point3 Target, double Distance, double Azimuth, double Elevation, double FieldOfView = 35)
{
    /// <summary>Elevação máxima (em 90° exatos o "para cima" da tela fica indefinido).</summary>
    public const double MaxElevation = 89.9;

    /// <summary>Ângulos (azimute, elevação) de cada vista pronta.</summary>
    /// <param name="lathe">Torno (eixo da peça deitado em X): a isométrica olha mais de frente, para o perfil.</param>
    public static (double Azimuth, double Elevation) AnglesOf(SceneView view, bool lathe = false) => view switch
    {
        SceneView.Isometric when lathe => (20, 25),
        SceneView.Top => (0, MaxElevation),
        SceneView.Front => (0, 0),
        SceneView.Side => (90, 0),
        _ => (35, 30),
    };

    /// <summary>Câmera que enquadra a caixa inteira ("ajustar"), na vista dada.</summary>
    public static SceneCamera Fit(Bounds3 box, SceneView view, double aspect, double fieldOfView = 35, bool lathe = false)
    {
        (double azimuth, double elevation) = AnglesOf(view, lathe);
        return Fit(box, azimuth, elevation, aspect, fieldOfView);
    }

    /// <summary>Câmera que enquadra a caixa inteira, nos ângulos dados.</summary>
    public static SceneCamera Fit(Bounds3 box, double azimuth, double elevation, double aspect, double fieldOfView = 35)
    {
        if (box.IsEmpty)
        {
            box = Bounds3.Empty.Include(new Point3(-50, -50, -50)).Include(new Point3(50, 50, 50));
        }

        var center = Point3.Lerp(box.Min, box.Max, 0.5);
        double radius = Math.Max(box.Min.DistanceTo(box.Max) / 2, 1);
        double half = fieldOfView / 2 * Math.PI / 180;

        // A esfera que envolve a caixa cabe na menor abertura (vertical, ou horizontal com a tela em pé)
        double distance = radius / Math.Sin(half) / Math.Min(1, Math.Max(aspect, 0.1)) * 1.02;
        return new SceneCamera(center, distance, azimuth, Math.Clamp(elevation, -MaxElevation, MaxElevation), fieldOfView);
    }

    /// <summary>Direção unitária do alvo para o olho.</summary>
    public Point3 ToEye
    {
        get
        {
            double az = Azimuth * Math.PI / 180, el = Math.Clamp(Elevation, -MaxElevation, MaxElevation) * Math.PI / 180;
            return new Point3(Math.Sin(az) * Math.Cos(el), -Math.Cos(az) * Math.Cos(el), Math.Sin(el));
        }
    }

    /// <summary>Posição do olho.</summary>
    public Point3 Eye
    {
        get
        {
            Point3 d = ToEye;
            return new Point3(Target.X + d.X * Distance, Target.Y + d.Y * Distance, Target.Z + d.Z * Distance);
        }
    }

    /// <summary>Gira a órbita (arrasto do dedo ou do mouse), em graus.</summary>
    public SceneCamera Orbit(double deltaAzimuth, double deltaElevation) => this with
    {
        Azimuth = ((Azimuth + deltaAzimuth) % 360 + 360) % 360,
        Elevation = Math.Clamp(Elevation + deltaElevation, -MaxElevation, MaxElevation),
    };

    /// <summary>Aproxima (fator &lt; 1) ou afasta (fator &gt; 1).</summary>
    public SceneCamera Zoom(double factor) => this with { Distance = Math.Clamp(Distance * factor, 1e-3, 1e7) };

    /// <summary>Desloca o alvo no plano da tela (dx para a direita, dy para cima), em mm.</summary>
    public SceneCamera Pan(double dx, double dy)
    {
        (Point3 right, Point3 up, _) = Axes();
        return this with
        {
            Target = new Point3(Target.X + right.X * dx + up.X * dy, Target.Y + right.Y * dx + up.Y * dy, Target.Z + right.Z * dx + up.Z * dy),
        };
    }

    /// <summary>Eixos da câmera: direita, cima e para trás (do alvo para o olho), unitários.</summary>
    public (Point3 Right, Point3 Up, Point3 Back) Axes()
    {
        Point3 back = ToEye;
        Point3 right = Rendering.ViewCamera.Normalize(Rendering.ViewCamera.Cross(new Point3(0, 0, 1), back));
        Point3 up = Rendering.ViewCamera.Cross(back, right);
        return (right, up, back);
    }

    /// <summary>Planos de recorte perto e longe, proporcionais à distância.</summary>
    public (double Near, double Far) ClipPlanes => (Distance / 100, Distance * 20);

    /// <summary>Matriz projeção × vista (4×4, por colunas, como o OpenGL espera).</summary>
    public float[] ViewProjection(double aspect)
    {
        (Point3 s, Point3 u, Point3 b) = Axes();
        Point3 e = Eye;
        double[] view =
        [
            s.X, u.X, b.X, 0,
            s.Y, u.Y, b.Y, 0,
            s.Z, u.Z, b.Z, 0,
            -(s.X * e.X + s.Y * e.Y + s.Z * e.Z), -(u.X * e.X + u.Y * e.Y + u.Z * e.Z), -(b.X * e.X + b.Y * e.Y + b.Z * e.Z), 1,
        ];

        (double near, double far) = ClipPlanes;
        double f = 1 / Math.Tan(FieldOfView * Math.PI / 180 / 2);
        double[] projection =
        [
            f / Math.Max(aspect, 1e-6), 0, 0, 0,
            0, f, 0, 0,
            0, 0, (far + near) / (near - far), -1,
            0, 0, 2 * far * near / (near - far), 0,
        ];

        var result = new float[16];
        for (int column = 0; column < 4; column++)
        {
            for (int row = 0; row < 4; row++)
            {
                double sum = 0;
                for (int k = 0; k < 4; k++)
                {
                    sum += projection[k * 4 + row] * view[column * 4 + k];
                }

                result[column * 4 + row] = (float)sum;
            }
        }

        return result;
    }
}
