namespace PcgUtil.Core;

/// <summary>
/// KRONOS effect-type names, index 0-197, transcribed from the KRONOS Parameter Guide
/// (E10) "Effect Guide". 0 = no effect; 109-140 are the "A - B" serial pairs, 141-185
/// the "A // B" parallel (mono/mono) chains, 186-197 the premium modeled effects.
/// Names use the guide's own abbreviations, so they read the same as on the instrument.
/// </summary>
public static class EffectNames
{
    private static readonly string[] Names =
    {
        "No Effect",   // 000
        "Stereo Dyna Compressor",   // 001
        "Stereo Compressor",   // 002
        "Stereo Expander",   // 003
        "St.Multiband Compressor",   // 004
        "Stereo Limiter",   // 005
        "Multiband Limiter",   // 006
        "Stereo Multiband Limiter",   // 007
        "Stereo Mastering Limiter",   // 008
        "Stereo Gate",   // 009
        "Stereo Noise Reduction",   // 010
        "Stereo Parametric 4EQ",   // 011
        "Stereo Graphic 7EQ",   // 012
        "Stereo Master 3EQ",   // 013
        "Stereo Exciter/Enhncr",   // 014
        "Stereo Isolator",   // 015
        "Stereo Wah/Auto Wah",   // 016
        "St. Vintage/Custom Wah",   // 017
        "Stereo Random Filter",   // 018
        "Multi Mode Filter",   // 019
        "Stereo Sub Oscillator",   // 020
        "Talking Modulator",   // 021
        "Stereo Decimator",   // 022
        "Stereo Analog Record",   // 023
        "Stereo Wave Shaper",   // 024
        "Piano Body/Damper",   // 025
        "Vocoder",   // 026
        "OD/Hi-Gain Wah",   // 027
        "OD/HyperGain Wah",   // 028
        "Stereo Guitar Cabinet",   // 029
        "Guitar Amp Model +P4EQ",   // 030
        "Guitar Amp Model +Cabinet",   // 031
        "Stereo Bass Cabinet",   // 032
        "Bass Amp Model",   // 033
        "Bass Amp Model +Cabinet",   // 034
        "Bass Amp TubeDrive +Cab",   // 035
        "Tube PreAmp Modeling",   // 036
        "St. Tube PreAmp Modeling",   // 037
        "Mic Modeling +PreAmp",   // 038
        "St. Mic Modeling +PreAmp",   // 039
        "Stereo Chorus",   // 040
        "Stereo Harmonic Chorus",   // 041
        "St. Bi-phase Modulation",   // 042
        "Multitap Cho/Delay 4Taps",   // 043
        "Multitap Cho/Delay 6Taps",   // 044
        "Bi Chorus",   // 045
        "Ensemble",   // 046
        "Polysix Ensemble",   // 047
        "Stereo Flanger",   // 048
        "Stereo Random Flanger",   // 049
        "Stereo Envelope Flanger",   // 050
        "Stereo Phaser",   // 051
        "Stereo Random Phaser",   // 052
        "Stereo Envelope Phaser",   // 053
        "Bi Phaser",   // 054
        "Stereo Vibrato",   // 055
        "Stereo Auto Fade Mod.",   // 056
        "2-Voice Resonator",   // 057
        "Doppler",   // 058
        "Scratch",   // 059
        "Grain Shifter",   // 060
        "Stereo Tremolo",   // 061
        "Stereo Envelope Tremolo",   // 062
        "Stereo Auto Pan",   // 063
        "Stereo Phaser+Tremolo",   // 064
        "Stereo Ring Modulator",   // 065
        "Stereo Frequency Shifter",   // 066
        "Detune",   // 067
        "Pitch Shifter",   // 068
        "Stereo Pitch Shifter",   // 069
        "Pitch Shifter BPM",   // 070
        "Stereo Pitch Shifter BPM",   // 071
        "Pitch Shift Mod.",   // 072
        "Organ Vibrato/Chorus",   // 073
        "Rotary Speaker",   // 074
        "Rotary Speaker Pro OD",   // 075
        "Rotary Speaker Pro CX",   // 076
        "L/C/R Delay",   // 077
        "L/C/R Long Delay",   // 078
        "Stereo/Cross Delay",   // 079
        "Stereo/Cross Long Delay",   // 080
        "Stereo Multitap Delay",   // 081
        "Stereo Modulation Delay",   // 082
        "Stereo Dynamic Delay",   // 083
        "Stereo Auto Panning Delay",   // 084
        "Tape Echo",   // 085
        "Multiband Mod. Delay",   // 086
        "Reverse Delay",   // 087
        "Hold Delay",   // 088
        "Auto Reverse",   // 089
        "Sequence BPM Delay",   // 090
        "L/C/R BPM Delay",   // 091
        "L/C/R BPM Long Delay",   // 092
        "Stereo BPM Delay",   // 093
        "Stereo BPM Long Delay",   // 094
        "Stereo BPM Multitap Delay",   // 095
        "Stereo BPM Mod. Delay",   // 096
        "St. BPM Auto Panning Dly",   // 097
        "Tape BPM Echo",   // 098
        "Reverse BPM Delay",   // 099
        "Overb",   // 100
        "Reverb Hall",   // 101
        "Reverb Smooth Hall",   // 102
        "Reverb Wet Plate",   // 103
        "Reverb Dry Plate",   // 104
        "Reverb Room",   // 105
        "Reverb Bright Room",   // 106
        "Early Reflections",   // 107
        "Early Reflections Hi Dens",   // 108
        "P4EQ - Exciter",   // 109
        "P4EQ - Wah",   // 110
        "P4EQ - Chorus/Flanger",   // 111
        "P4EQ - Phaser",   // 112
        "P4EQ - Multitap Delay",   // 113
        "Comp - Wah",   // 114
        "Comp - Amp Sim",   // 115
        "Comp - OD/HiGain",   // 116
        "Comp - P4EQ",   // 117
        "Comp - Chorus/Flanger",   // 118
        "Comp - Phaser",   // 119
        "Comp - Multitap Delay",   // 120
        "Limiter - P4EQ",   // 121
        "Limiter - Chorus/Flanger",   // 122
        "Limiter - Phaser",   // 123
        "Limiter - Multitap Delay",   // 124
        "Exciter - Comp",   // 125
        "Exciter - Limiter",   // 126
        "Exciter - Chorus/Flanger",   // 127
        "Exciter - Phaser",   // 128
        "Exciter - Multitap Delay",   // 129
        "OD/Hi Gain - Amp Sim",   // 130
        "OD/Hi Gain - Cho/Flanger",   // 131
        "OD/Hi Gain - Phaser",   // 132
        "OD/Hi Gain - Multitap Dly",   // 133
        "Wah - Amp Sim",   // 134
        "Decimator - Amp Sim",   // 135
        "Decimator - Comp",   // 136
        "Amp Sim - Tremolo",   // 137
        "Cho/Flanger - Multitap Dly",   // 138
        "Phaser - Chorus/Flanger",   // 139
        "Reverb - Gate",   // 140
        "P4EQ // P4EQ",   // 141
        "P4EQ // Comp",   // 142
        "P4EQ // Limiter",   // 143
        "P4EQ // Exciter",   // 144
        "P4EQ // OD/Hi Gain",   // 145
        "P4EQ // Wah",   // 146
        "P4EQ // Chorus/Flanger",   // 147
        "P4EQ // Phaser",   // 148
        "P4EQ // Multitap BPM Dly",   // 149
        "Comp // Comp",   // 150
        "Comp // Limiter",   // 151
        "Comp // Exciter",   // 152
        "Comp // OD/Hi Gain",   // 153
        "Comp // Wah",   // 154
        "Comp // Chorus/Flanger",   // 155
        "Comp // Phaser",   // 156
        "Comp // Multitap BPM Dly",   // 157
        "Limiter // Limiter",   // 158
        "Limiter // Exciter",   // 159
        "Limiter // OD/Hi Gain",   // 160
        "Limiter // Wah",   // 161
        "Limiter // Chorus/Flanger",   // 162
        "Limiter // Phaser",   // 163
        "Limiter // Mtap BPM Dly",   // 164
        "Exciter // Exciter",   // 165
        "Exciter // OD/Hi Gain",   // 166
        "Exciter // Wah",   // 167
        "Exciter // Chorus/Flanger",   // 168
        "Exciter // Phaser",   // 169
        "Exciter // Mtap BPM Dly",   // 170
        "OD/Hi Gain // OD/Hi Gain",   // 171
        "OD/Hi Gain // Wah",   // 172
        "OD/Hi Gain // Cho/Flanger",   // 173
        "OD/Hi Gain // Phaser",   // 174
        "OD/Hi Gain // Mt BPM Dly",   // 175
        "Wah // Wah",   // 176
        "Wah // Chorus/Flanger",   // 177
        "Wah // Phaser",   // 178
        "Wah // Multitap BPM Dly",   // 179
        "Cho/Flange // Cho/Flanger",   // 180
        "Cho/Flange // Phaser",   // 181
        "Cho/Flange // Mt BPM Dly",   // 182
        "Phaser // Phaser",   // 183
        "Phaser // Mtap BPM Dly",   // 184
        "Mt.BPM Dly // Mt.BPM Dly",   // 185
        "Small Phase",   // 186
        "Orange Phase",   // 187
        "Black Phase",   // 188
        "Vintage Chorus",   // 189
        "Black Chorus",   // 190
        "EP Chorus",   // 191
        "Vintage Flanger",   // 192
        "Red Comp",   // 193
        "Vox Wah",   // 194
        "Stereo EP Cabinet",   // 195
        "Rotary Speaker Amp Model",   // 196
        "Rotary Speaker Pro CX Custom",   // 197
    };

    /// <summary>Number of known effect types (0..Count-1).</summary>
    public static int Count => Names.Length;

    /// <summary>Name for an effect-type id; "Effect NNN" for anything outside the table
    /// so an unknown id still shows its (always-correct) number.</summary>
    public static string Name(int id) =>
        id >= 0 && id < Names.Length ? Names[id] : $"Effect {id:D3}";
}
