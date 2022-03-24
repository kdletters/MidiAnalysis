using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http.Json;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace MidiAnalys
{
    class Program
    {
        private const string Path = @"E:\Desktop\test.mid";

        static async Task Main(string[] args)
        {
            for (int j = 0; j < args.Length; j++)
            {
                var file = new FileInfo(args[j]);
                if (file.Extension != ".mid") continue;
                var fs   = file.OpenRead();
                var temp = new byte[4];
                await fs.ReadAsync(temp.AsMemory(0, 4));
                await fs.ReadAsync(temp.AsMemory(0, 4));
                await fs.ReadAsync(temp.AsMemory(0, 2));
                var mode = temp[0] * 16 + temp[1];
                await fs.ReadAsync(temp.AsMemory(0, 2));
                var trackCount = temp[0] * 16 + temp[1];
                await fs.ReadAsync(temp.AsMemory(0, 2));
                Note.ticksInCrotchet = temp[0] * 16 + temp[1];
                byte[] buffer = Array.Empty<byte>();
                for (int i = 0; i < 2; i++)
                {
                    await fs.ReadAsync(temp.AsMemory(0, 4));
                    await fs.ReadAsync(temp.AsMemory(0, 4));

                    buffer = new byte[
                        temp[0] * 256 * 256 * 256 +
                        temp[1] * 256 * 256 +
                        temp[2] * 256 +
                        temp[3]
                    ];
                    await fs.ReadAsync(buffer.AsMemory(0, buffer.Length));
                    Analyse(buffer);
                }

                await fs.ReadAsync(temp.AsMemory(0, 4));
                await fs.ReadAsync(temp.AsMemory(0, 4));

                buffer = new byte[
                    temp[0] * 256 * 256 * 256 +
                    temp[1] * 256 * 256 +
                    temp[2] * 256 +
                    temp[3]
                ];
                await fs.ReadAsync(buffer.AsMemory(0, buffer.Length));
                var nodeList = Analyse(buffer);

                await fs.ReadAsync(temp.AsMemory(0, 4));
                await fs.ReadAsync(temp.AsMemory(0, 4));

                buffer = new byte[
                    temp[0] * 256 * 256 * 256 +
                    temp[1] * 256 * 256 +
                    temp[2] * 256 +
                    temp[3]
                ];
                await fs.ReadAsync(buffer.AsMemory(0, buffer.Length));
                var slipNodeList = Analyse(buffer);

                Console.WriteLine($"bpm is {Note.bpm}");
                Console.WriteLine($"beat is {Note.beat.Key}/{Note.beat.Value}");
                Console.WriteLine($"ticksInCrotchet is {Note.ticksInCrotchet}");
                Console.WriteLine($"node count is {nodeList.Count}");
                Console.WriteLine(string.Join("\n", nodeList));

                var list = nodeList.Select(x => new MusicNoteData(NoteType.Click, MusicNoteData.GetClickIndex(x.pitch), x.tick * 60f / Note.bpm / Note.ticksInCrotchet, 1)).ToList();
                list.AddRange(slipNodeList.Select(x => new MusicNoteData(NoteType.Combo, MusicNoteData.GetClickIndex(x.pitch), x.tick * 60f / Note.bpm / Note.ticksInCrotchet, 1)).ToList());
                list = list.OrderBy(x => x.timeStart).ToList();
                var output = File.Create(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), $"{file.Name[..^file.Extension.Length]}.json"));
                output.Write(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(list)));
                output.Close();
            }
        }

        private static unsafe List<Note> Analyse(byte[] buffer)
        {
            var nodeList     = new List<Note>();
            var bufferLength = buffer.Length;

            Span<byte> temp     = stackalloc byte[4];
            var        index    = 0;
            var        phase    = 0;
            var        tick     = 0;
            byte       pitch    = 0;
            byte       velocity = 0;

            for (int i = 0; i < bufferLength; i++)
            {
                if (buffer[i] == 0xFF &&
                    buffer[i + 1] == 0x2F &&
                    buffer[i + 2] == 0x00) break;

                switch (phase)
                {
                    case 0:
                    {
                        if ((buffer[i] & 0b_1000_0000) == 0b_0000_0000)
                            phase++;

                        temp[index++] = buffer[i];
                        break;
                    }
                    case 1:
                    {
                        switch (buffer[i])
                        {
                            case 0x90:
                            {
                                phase++;
                                break;
                            }
                            case 0x80:
                            case 0xa0:
                            case 0xb0:
                            case 0xe0:
                            {
                                phase =  0;
                                i     += 2;
                                break;
                            }
                            case 0xc0:
                            case 0xd0:
                            {
                                phase = 0;
                                i++;
                                break;
                            }
                            case 0xFF:
                            {
                                switch (buffer[i + 1])
                                {
                                    case 0x51:
                                    {
                                        var tempo = buffer[i + +3] * 256 * 256 +
                                                    buffer[i + +4] * 256 +
                                                    buffer[i + +5];
                                        Note.bpm = 60000000 / tempo;
                                        break;
                                    }
                                    case 0x58:
                                        Note.beat = new KeyValuePair<int, int>(buffer[i + +3], (int) Math.Pow(2, buffer[i + +4]));
                                        break;
                                }

                                phase =  0;
                                i     += buffer[i + 2] + 2;
                                break;
                            }
                            default:
                            {
                                phase = 0;
                                break;
                            }
                        }

                        tick += index switch
                        {
                            4 => temp[0] * 256 * 256 * 256 +
                                 temp[1] * 256 * 256 +
                                 temp[2] * 256 +
                                 temp[3],
                            3 => temp[0] * 256 * 256 +
                                 temp[1] * 256 +
                                 temp[2],
                            2 => temp[0] * 256 +
                                 temp[1],
                            1 => temp[0],
                            0 => 0
                        };

                        temp[0] = 0;
                        temp[1] = 0;
                        temp[2] = 0;
                        temp[3] = 0;

                        index = 0;
                        break;
                    }
                    case 2:
                    {
                        pitch = buffer[i];
                        phase++;
                        break;
                    }
                    case 3:
                    {
                        velocity = buffer[i];
                        nodeList.Add(new Note(tick, pitch, velocity));
                        phase = 0;
                        break;
                    }
                }
            }

            // var count = nodeList.Count;
            // for (int i = count - 1; i >= 0; i--)
            // {
            //     if (nodeList[i].velocity == 0) nodeList.RemoveAt(i);
            // }

            return nodeList;
        }
    }

    public record Note(int tick, byte pitch, byte velocity)
    {
        public static int bpm = 120;
        public static int ticksInCrotchet; //四分音符的tick数
        public static KeyValuePair<int, int> beat;

        public override string ToString()
        {
            var noteNum = (float) tick / ticksInCrotchet * beat.Value / 4;
            return $"{nameof(tick)}: {tick,10}, bar: {(int) noteNum / beat.Key,3}-{noteNum % beat.Key,-4}, {nameof(pitch)}: {pitch,2}, {nameof(velocity)}: {velocity}";
        }
    }


    public class MusicNoteData
    {
        /// <summary>
        /// 类型
        /// </summary>
        public NoteType type;

        public int index;

        /// <summary>
        /// 开始时间
        /// </summary>
        public float timeStart;

        /// <summary>
        /// 消耗时间
        /// </summary>
        public float timeConsuming;

        public MusicNoteData(NoteType type, int index, float timeStart, float timeConsuming)
        {
            this.type          = type;
            this.index         = index;
            this.timeStart     = timeStart;
            this.timeConsuming = timeConsuming;
        }

        public static int GetClickIndex(int pitch)
        {
            var index = pitch switch
            {
                48 => 1,
                50 => 2,
                52 => 3,
                53 => 4,
                55 => 5,
                57 => 6,
                59 => 7,
                _  => 1
            };
            var offset            = (pitch - 48) / 12 * 7;
            if (offset > 0) index += offset;
            return index;
        }
    }

    /// <summary>
    /// 音符类型
    /// </summary>
    public enum NoteType
    {
        /// <summary>
        /// 单击
        /// </summary>
        Click,

        /// <summary>
        /// 连击
        /// </summary>
        Combo
    }
}