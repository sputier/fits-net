namespace Eleris.FitsNet.Wcs;

/// <summary>
/// A spectral coordinate axis (standard section 8.4, Greisen et al. 2006): frequency, energy, wavenumber, radio velocity,
/// wavelength, optical velocity, redshift, air wavelength, apparent radial velocity or beta, sampled linearly, logarithmically
/// (<c>-LOG</c>) or linearly in another spectral variable (<c>-F2W</c> and the other codes of Table 26).
/// </summary>
internal sealed class SpectralAxis
{
    private const double C = 2.99792458e8;      // speed of light (m/s)
    private const double H = 6.6260755e-34;     // Planck constant (J s), the value of the reference implementation

    private readonly string _type;
    private readonly char _pClass;               // F, W, A or V: the class of the coordinate the axis is expressed in
    private readonly char _xClass;               // the class it is sampled in; '\0' linear, 'L' logarithmic
    private readonly double _scale;              // unit of the axis in SI units
    private readonly double _restFrq, _restWav;
    private readonly double _crvalSi;
    private readonly double _crval;   // as written in the header, in the unit of the axis
    private readonly double _crvalX;
    private readonly double _dXdS;

    private SpectralAxis(string type, char pClass, char xClass, double scale, double restFrq, double restWav, double crval)
    {
        (_type, _pClass, _xClass, _scale, _restFrq, _restWav) = (type, pClass, xClass, scale, restFrq, restWav);
        _crvalSi = crval * scale;
        _crval = crval;
        if (xClass == 'L' && crval == 0.0) throw new FitsException("A logarithmic spectral axis needs a reference value that is not zero.");
        if (xClass is not ('\0' or 'L'))
        {
            _crvalX = SpectralToX(_crvalSi);
            if (!double.IsFinite(_crvalX)) throw new FitsException("The reference value of the spectral axis cannot be converted (zero or invalid frequency or wavelength).");
            _dXdS = Derivative();
            if (!double.IsFinite(_dXdS) || _dXdS == 0.0) throw new FitsException("The spectral algorithm is singular at the reference value.");
        }
    }

    private static char ClassOf(string type) => type switch
    {
        "FREQ" or "ENER" or "WAVN" or "VRAD" => 'F',
        "WAVE" or "VOPT" or "ZOPT" => 'W',
        "AWAV" => 'A',
        "VELO" or "BETA" => 'V',
        _ => '\0',
    };

    private static bool NeedsRest(string type) => type is "VRAD" or "VOPT" or "ZOPT" or "VELO" or "BETA";

    /// <summary>Creates the axis when <paramref name="ctype"/> names a spectral coordinate; null otherwise.</summary>
    public static SpectralAxis? TryCreate(string ctype, double crval, FitsHeader header, string alternate, int axis)
    {
        if (ctype.Length < 4) return null;
        string type = ctype[..4];
        char pClass = ClassOf(type);
        if (pClass == '\0') return null;

        // Anything that is not a spectral type followed by nothing or by "-" and a known algorithm code is an ordinary linear axis,
        // as in FREQUENCY, VELOCITY, WAVELENGTH or FREQ-LSR.
        string rest = ctype.Length > 4 ? ctype[4..].TrimEnd() : "";
        char xClass = '\0';
        if (rest.Length > 0)
        {
            if (rest.Length != 4 || rest[0] != '-') return null;
            string code = rest[1..];
            if (code == "LOG") xClass = 'L';
            else if (code is "GRI" or "GRA" or "TAB") throw new NotSupportedException($"The spectral algorithm '{code}' (CTYPE{axis}) is not supported.");
            else if (code.Length == 3 && code[1] == '2' && "FWAV".Contains(code[0]) && "FWAV".Contains(code[2]) && code[0] != code[2])
            {
                if (code[2] != pClass)
                    throw new FitsException($"CTYPE{axis} '{ctype}': the algorithm {code} does not produce a {type} coordinate.");
                xClass = code[0];
            }
            else return null;
        }

        double restFrq = header.TryGetValue("RESTFRQ" + alternate, out double rf) ? rf : alternate.Length == 0 && header.TryGetValue("RESTFREQ", out double rf2) ? rf2 : 0.0;
        double restWav = header.TryGetValue("RESTWAV" + alternate, out double rw) ? rw : 0.0;
        // the rest frequency or wavelength is only needed to convert: a linearly sampled axis keeps its own values
        bool needRest = xClass is not ('\0' or 'L') && (NeedsRest(type) || xClass == 'V' || pClass == 'V');
        if (xClass is '\0' or 'L') needRest = false;
        if (needRest)
        {
            if (restFrq == 0.0 && restWav == 0.0)
                throw new FitsException($"CTYPE{axis} '{ctype}' needs RESTFRQ or RESTWAV.");
            if (restFrq == 0.0) restFrq = C / restWav;
            else restWav = C / restFrq;
        }

        string unit = (header.Get<string>("CUNIT" + axis + alternate) ?? "").Trim();
        return new SpectralAxis(type, pClass, xClass, UnitScale(type, unit), restFrq, restWav, crval);
    }

    private static double UnitScale(string type, string unit)
    {
        if (unit.Length == 0) return 1.0;
        double? scale = ClassOf(type) switch
        {
            _ when type is "ZOPT" or "BETA" => null,
            _ when type == "ENER" => unit switch { "J" => 1.0, "eV" => 1.602176634e-19, "keV" => 1.602176634e-16, "MeV" => 1.602176634e-13, "erg" => 1e-7, _ => null },
            _ when type == "WAVN" => unit switch { "m-1" or "/m" or "1/m" => 1.0, "cm-1" or "/cm" => 100.0, "mm-1" or "/mm" => 1000.0, "um-1" or "/um" => 1e6, _ => null },
            _ when type is "VRAD" or "VOPT" or "VELO" => unit switch { "m/s" or "m s-1" => 1.0, "km/s" or "km s-1" => 1e3, "cm/s" => 1e-2, _ => null },
            'F' => unit switch { "Hz" => 1.0, "kHz" => 1e3, "MHz" => 1e6, "GHz" => 1e9, "THz" => 1e12, _ => null },
            'W' or 'A' => unit switch { "m" => 1.0, "cm" => 1e-2, "mm" => 1e-3, "um" or "micron" => 1e-6, "nm" => 1e-9, "Angstrom" or "angstrom" => 1e-10, "km" => 1e3, _ => null },
            _ => null,
        };
        return scale ?? throw new FitsException($"The unit '{unit}' is not valid for a {type} axis.");
    }

    // ---- Conversions between the spectral types and the variables of their class ----------------------------------

    private double TypeToVariable(double s) => _type switch
    {
        "ENER" => s / H,
        "WAVN" => s * C,
        "VRAD" => _restFrq * (1.0 - s / C),
        "VOPT" => _restWav * (1.0 + s / C),
        "ZOPT" => _restWav * (1.0 + s),
        "BETA" => s * C,
        _ => s,
    };

    private double VariableToType(double v) => _type switch
    {
        "ENER" => v * H,
        "WAVN" => v / C,
        "VRAD" => C * (_restFrq - v) / _restFrq,
        "VOPT" => C * (v - _restWav) / _restWav,
        "ZOPT" => (v - _restWav) / _restWav,
        "BETA" => v / C,
        _ => v,
    };

    private static double AirToVacuum(double air)
    {
        if (air == 0.0) return double.NaN;
        double s = 1.0 / air;
        s *= s;
        double n = 2.554e8 / (0.41e14 - s) + 294.981e8 / (1.46e14 - s) + 1.000064328;
        return air * n;
    }

    private static double VacuumToAir(double vacuum)
    {
        if (vacuum == 0.0) return double.NaN;
        double n = 1.0;
        for (int k = 0; k < 4; k++)
        {
            double s = n / vacuum;
            s *= s;
            n = 2.554e8 / (0.41e14 - s) + 294.981e8 / (1.46e14 - s) + 1.000064328;
        }
        return vacuum / n;
    }

    /// <summary>Frequency of a variable of the given class (NaN when it does not exist).</summary>
    private double ToFrequency(char cls, double v) => cls switch
    {
        'F' => v,
        'W' => v == 0.0 ? double.NaN : C / v,
        'A' => AirToVacuum(v) is var w && w != 0.0 ? C / w : double.NaN,
        _ => Math.Abs(v) >= C ? double.NaN : _restFrq * Math.Sqrt((C - v) / (C + v)),
    };

    private double FromFrequency(char cls, double nu) => cls switch
    {
        'F' => nu,
        'W' => nu == 0.0 ? double.NaN : C / nu,
        'A' => nu == 0.0 ? double.NaN : VacuumToAir(C / nu),
        _ => C * (_restFrq * _restFrq - nu * nu) / (_restFrq * _restFrq + nu * nu),
    };

    /// <summary>The variable the axis is sampled in, for a spectral coordinate in SI units.</summary>
    private double SpectralToX(double s) => FromFrequency(_xClass, ToFrequency(_pClass, TypeToVariable(s)));

    private double XToSpectral(double x) => VariableToType(FromFrequency(_pClass, ToFrequency(_xClass, x)));

    /// <summary>d(variable of the type) / d(spectral coordinate): the conversion between a type and the variable of its class.</summary>
    private double TypeDerivative() => _type switch
    {
        "ENER" => 1.0 / H,
        "WAVN" => C,
        "VRAD" => -_restFrq / C,
        "VOPT" => _restWav / C,
        "ZOPT" => _restWav,
        "BETA" => C,
        _ => 1.0,
    };

    /// <summary>d(vacuum wavelength) / d(air wavelength).</summary>
    private static double AirToVacuumDerivative(double air)
    {
        double s = 1.0 / (air * air);
        double a = 0.41e14 - s, b = 1.46e14 - s;
        double n = 2.554e8 / a + 294.981e8 / b + 1.000064328;
        double dndS = 2.554e8 / (a * a) + 294.981e8 / (b * b);
        return n + air * dndS * (-2.0 / (air * air * air));
    }

    /// <summary>d(frequency) / d(variable of the class).</summary>
    private double ToFrequencyDerivative(char cls, double v)
    {
        switch (cls)
        {
            case 'F': return 1.0;
            case 'W': return -C / (v * v);
            case 'A':
            {
                double vacuum = AirToVacuum(v);
                return -C / (vacuum * vacuum) * AirToVacuumDerivative(v);
            }
            default:
            {
                double up = C + v;
                return -_restFrq * C / (up * up) * Math.Sqrt(up / (C - v));
            }
        }
    }

    /// <summary>d(variable of the class) / d(frequency).</summary>
    private double FromFrequencyDerivative(char cls, double nu)
    {
        switch (cls)
        {
            case 'F': return 1.0;
            case 'W': return -C / (nu * nu);
            case 'A':
            {
                double vacuum = C / nu;
                double air = VacuumToAir(vacuum);
                return -C / (nu * nu) / AirToVacuumDerivative(air);
            }
            default:
            {
                double d = _restFrq * _restFrq + nu * nu;
                return -4.0 * C * nu * _restFrq * _restFrq / (d * d);
            }
        }
    }

    /// <summary>d(sampled variable) / d(spectral coordinate) at the reference value, by the chain rule through the frequency.</summary>
    private double Derivative()
    {
        double pVariable = TypeToVariable(_crvalSi);
        double nu = ToFrequency(_pClass, pVariable);
        return FromFrequencyDerivative(_xClass, nu) * ToFrequencyDerivative(_pClass, pVariable) * TypeDerivative();
    }

    // ---- Axis transformation --------------------------------------------------------------------------------------

    /// <summary>Intermediate world coordinate (in the unit of the axis, relative to the reference value) to the spectral coordinate.</summary>
    public double IntermediateToWorld(double w)
    {
        if (_xClass == '\0') return _crval + w;
        double wSi = w * _scale;
        if (_xClass == 'L') return _crvalSi * Math.Exp(wSi / _crvalSi) / _scale;
        return XToSpectral(_crvalX + wSi * _dXdS) / _scale;
    }

    /// <summary>The inverse of <see cref="IntermediateToWorld"/>; NaN when the coordinate has no image.</summary>
    public double WorldToIntermediate(double world)
    {
        if (_xClass == '\0') return world - _crval;
        double sSi = world * _scale;
        if (_xClass == 'L') return sSi > 0.0 == _crvalSi > 0.0 && sSi != 0.0 ? _crvalSi * Math.Log(sSi / _crvalSi) / _scale : double.NaN;
        return (SpectralToX(sSi) - _crvalX) / _dXdS / _scale;
    }
}
