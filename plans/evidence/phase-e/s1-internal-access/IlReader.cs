using System.Reflection;
using System.Reflection.Emit;

namespace InternalAccess;

internal readonly record struct Instruction(int Offset, OpCode OpCode, int? Token);

/// <summary>Walks a method body's IL, one instruction at a time.</summary>
internal static class IlReader
{
	private static readonly OpCode?[] OneByte = new OpCode?[256];
	private static readonly OpCode?[] TwoByte = new OpCode?[256];

	static IlReader()
	{
		foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
		{
			if (field.GetValue(null) is not OpCode opCode) continue;
			var value = unchecked((ushort)opCode.Value);
			if (value < 0x100) OneByte[value] = opCode;
			else TwoByte[value & 0xFF] = opCode;
		}
	}

	internal static IEnumerable<Instruction> Read(byte[] il)
	{
		var position = 0;
		while (position < il.Length)
		{
			var offset = position;
			var first = il[position++];
			OpCode opCode;
			if (first == 0xFE)
			{
				if (position >= il.Length) yield break;
				var second = il[position++];
				opCode = TwoByte[second] ?? throw new BadImageFormatException($"Unknown opcode FE {second:X2} at {offset}.");
			}
			else
			{
				opCode = OneByte[first] ?? throw new BadImageFormatException($"Unknown opcode {first:X2} at {offset}.");
			}

			int? token = null;
			switch (opCode.OperandType)
			{
				case OperandType.InlineNone:
					break;
				case OperandType.ShortInlineI:
				case OperandType.ShortInlineVar:
				case OperandType.ShortInlineBrTarget:
					position += 1;
					break;
				case OperandType.InlineVar:
					position += 2;
					break;
				case OperandType.InlineI:
				case OperandType.InlineBrTarget:
				case OperandType.InlineString:
				case OperandType.InlineSig:
					position += 4;
					break;
				case OperandType.ShortInlineR:
					position += 4;
					break;
				case OperandType.InlineI8:
				case OperandType.InlineR:
					position += 8;
					break;
				case OperandType.InlineSwitch:
				{
					var count = BitConverter.ToInt32(il, position);
					position += 4 + 4 * count;
					break;
				}
				case OperandType.InlineMethod:
				case OperandType.InlineField:
				case OperandType.InlineType:
				case OperandType.InlineTok:
					token = BitConverter.ToInt32(il, position);
					position += 4;
					break;
				default:
					throw new NotSupportedException(opCode.OperandType.ToString());
			}

			yield return new Instruction(offset, opCode, token);
		}
	}
}
