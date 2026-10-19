using System;

namespace APXRuntime.MatchPort;

internal static class TransferWire
{
	internal static bool ReadTransferFields(byte[] data, out int amount, out long assisted)
	{
		amount = 0;
		assisted = 0L;
		if (data == null || data.Length > 1048576)
		{
			return false;
		}
		int p = 0;
		try
		{
			while (p < data.Length)
			{
				ulong num = Varint(data, ref p);
				int num2 = checked((int)(num >> 3));
				int num3 = (int)(num & 7);
				if (num2 <= 0)
				{
					return false;
				}
				if (num2 == 5 && num3 == 5)
				{
					if (p > data.Length - 4)
					{
						return false;
					}
					amount = BitConverter.ToInt32(data, p);
					p += 4;
				}
				else if (num2 == 7 && num3 == 1)
				{
					if (p > data.Length - 8)
					{
						return false;
					}
					assisted = BitConverter.ToInt64(data, p);
					p += 8;
				}
				else
				{
					checked
					{
						switch (num3)
						{
						case 0:
							Varint(data, ref p);
							break;
						case 1:
							p += 8;
							break;
						case 2:
						{
							int num4 = (int)Varint(data, ref p);
							p += num4;
							break;
						}
						case 5:
							p += 4;
							break;
						default:
							return false;
						}
					}
				}
				if (p > data.Length)
				{
					return false;
				}
			}
			return amount > 0;
		}
		catch (Exception)
		{
			return false;
		}
	}

	private static ulong Varint(byte[] data, ref int p)
	{
		ulong num = 0uL;
		for (int i = 0; i < 64; i += 7)
		{
			if (p >= data.Length)
			{
				throw new FormatException();
			}
			byte b = data[p++];
			if (i == 63 && (b & 0xFEu) != 0)
			{
				throw new FormatException();
			}
			num |= (ulong)((long)(b & 0x7F) << i);
			if ((b & 0x80) == 0)
			{
				return num;
			}
		}
		throw new FormatException();
	}
}
