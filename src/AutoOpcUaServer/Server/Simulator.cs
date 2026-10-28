using AutoOpcUaServer.Model;

namespace AutoOpcUaServer.Server;

/// <summary>Computes the next value of a simulated tag. Pure logic, no OPC UA types.</summary>
public sealed class Simulator
{
    public const double DefaultMin = 0;
    public const double DefaultMax = 100;
    public const double DefaultPeriodSeconds = 60;

    private readonly TagDefinition _tag;
    private readonly Random _random;
    private readonly double _min;
    private readonly double _max;
    private readonly double _period;
    private double _counter;
    private bool _toggle;

    public Simulator(TagDefinition tag, Random? random = null)
    {
        _tag = tag;
        _random = random ?? Random.Shared;

        _min = tag.Min ?? (tag.DataType == TagDataType.Boolean ? 0 : DefaultMin);
        _max = tag.Max ?? (tag.DataType == TagDataType.Boolean ? 1 : DefaultMax);
        if (TagDataTypes.IsInteger(tag.DataType))
        {
            // Keep the defaults inside the range of small or unsigned types.
            var (typeMin, typeMax) = ValueConverter.IntegerRange(tag.DataType);
            _min = Math.Clamp(_min, typeMin, typeMax);
            _max = Math.Clamp(_max, typeMin, typeMax);
        }

        _period = tag.Period ?? DefaultPeriodSeconds;
        _counter = _min;

        if (ValueConverter.TryParseDouble(tag.Value, out var start) && start >= _min && start <= _max)
        {
            _counter = start;
        }
    }

    public TagDefinition Tag => _tag;

    /// <summary>Returns the value for <paramref name="elapsedSeconds"/> since the server start.</summary>
    public object Next(double elapsedSeconds)
    {
        double value;
        switch (_tag.Simulation)
        {
            case SimulationKind.Random:
                if (_tag.DataType == TagDataType.Boolean)
                {
                    return _random.Next(2) == 1;
                }

                value = TagDataTypes.IsInteger(_tag.DataType)
                    ? Math.Floor(_min + _random.NextDouble() * (_max - _min + 1))
                    : _min + _random.NextDouble() * (_max - _min);
                value = Math.Min(value, _max);
                break;

            case SimulationKind.Sine:
                var amplitude = (_max - _min) / 2;
                value = _min + amplitude + amplitude * Math.Sin(2 * Math.PI * elapsedSeconds / _period);
                break;

            case SimulationKind.Ramp:
                value = _min + (elapsedSeconds % _period) / _period * (_max - _min);
                break;

            case SimulationKind.Counter:
                _counter++;
                if (_counter > _max)
                {
                    _counter = _min;
                }

                value = _counter;
                break;

            case SimulationKind.Toggle:
                _toggle = !_toggle;
                if (_tag.DataType == TagDataType.Boolean)
                {
                    return _toggle;
                }

                value = _toggle ? _max : _min;
                break;

            default:
                throw new InvalidOperationException($"Tag '{_tag.Path}' is not simulated.");
        }

        return ValueConverter.FromDouble(value, _tag.DataType);
    }
}
