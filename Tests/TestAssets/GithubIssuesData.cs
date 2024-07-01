using Unity.Mathematics;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public static class GithubIssuesData
    {
        // Input for this test is grabbed from issue #30 from @mduvergey user.
        // https://github.com/andywiecko/BurstTriangulator/issues/30
        // This test tests editor hanging problem reported in issue #30 and #31.
        //
        // UPDATE: Thanks to the recent fix, this input will no longer cause the algorithm to hang,
        //         unless "max iters" are intentionally reduced.
        public static readonly (double2[] points, int[] constraints) Issue30 = (
            points: new double2[]
            {
                new (14225.59f, -2335.27f), new (13380.24f, -2344.72f), new (13197.35f, -2119.65f),
                new (11750.51f, -2122.18f), new (11670.1f, -2186.25f), new (11424.88f, -2178.53f),
                new (11193.54f, -2025.36f), new (11159.36f, -1812.22f), new (10956.29f, -1731.62f),
                new (10949.03f, -1524.29f), new (10727.71f, -1379.53f), new (10532.48f, -1145.83f),
                new (10525.18f, -906.69f), new (10410.57f, -750.73f), new (10629.48f, -657.33f),
                new (10622f, -625.7f), new (10467.05f, -552.15f), new (10415.75f, -423.21f),
                new (10037.01f, -427.11f), new (9997.4f, -487.33f), new (9788.02f, -539.44f),
                new (9130.03f, -533.95f), new (8905.69f, -490.95f), new (8842.1f, -396.11f),
                new (8410.81f, -407.12f), new (8211.88f, -583.32f), new (7985.37f, -588.47f),
                new (7880.46f, -574.94f), new (7200.87f, -574.14f), new (6664.29f, -637.89f),
                new (6351.84f, -483.61f), new (6324.37f, -143.48f), new (6093.94f, -152.8f),
                new (5743.03f, 213.65f), new (5725.63f, 624.21f), new (5562.64f, 815.17f),
                new (5564.65f, 1145.66f), new (4846.4f, 1325.89f), new (4362.98f, 1327.97f),
                new (5265.89f, 267.31f), new (5266.32f, -791.39f), new (3806f, -817.38f),
                new (3385.84f, -501.25f), new (3374.35f, -372.48f), new (3555.98f, -321.11f),
                new (3549.9f, -272.35f), new (3356.27f, -221.45f), new (3352.42f, 13.16f),
                new (1371.39f, 5.41f), new (1362.47f, -191.23f), new (1188.9f, -235.72f),
                new (1180.86f, -709.59f), new (132.26f, -720.07f), new (1.94f, -788.66f),
                new (-1240.12f, -779.03f), new (-1352.26f, -973.64f), new (-1665.17f, -973.84f),
                new (-1811.91f, -932.75f), new (-1919.98f, -772.61f), new (-2623.09f, -782.31f),
                new (-3030.54f, -634.38f), new (-3045.53f, -52.71f), new (-3969.83f, -61.28f),
                new (-6676.96f, 102.16f), new (-7209.27f, 100.12f), new (-7729.39f, 178.02f),
                new (-8228.73f, 126.39f), new (-8409.52f, 164.47f), new (-9432.81f, 168.43f),
                new (-9586.02f, 116.14f), new (-10758.65f, 110.23f), new (-10894.94f, 63.53f),
                new (-11737.45f, 60.54f), new (-11935.7f, 1.79f), new (-12437.14f, -4.33f),
                new (-12832.19f, 41.15f), new (-13271.23f, 30.64f), new (-13478.52f, 65.91f),
                new (-13729f, 65.71f), new (-13846.23f, 21.68f), new (-14000.3f, 62.86f),
                new (-15224.52f, 58.78f), new (-15232.59f, -142.28f), new (-4326.12f, -232.09f),
                new (-4083.7f, -441.37f), new (-3467.35f, -478.48f), new (-3040.92f, -1160.16f),
                new (7192.14f, -1332.7f), new (7249.66f, -939.11f), new (8399.41f, -932.84f),
                new (8816.72f, -830.49f), new (9861.58f, -835.71f), new (10065.59f, -1110.57f),
                new (10052.32f, -2118.14f), new (9006.64f, -2125.78f), new (8818.37f, -2203.58f),
                new (8846.09f, -2620.2f), new (14244.65f, -2650.96f)
            },
            constraints: new[]
            {
                97, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14, 15, 15, 16, 16, 17, 17, 18,
                18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26, 27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33,
                33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38, 39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48,
                48, 49, 49, 50, 50, 51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 62, 62, 63,
                63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74, 75, 75, 76, 76, 77, 77, 78,
                78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86, 87, 87, 88, 88, 89, 89, 90, 90, 91, 91, 92, 92, 93,
                93, 94, 94, 95, 95, 96, 96, 97
            }
        );

        public static readonly (double2[] points, int[] constraints) Issue31 = (
            points: new double2[]
            {
                new (31.28938f, 37.67612f), new (31.79285f, 37.00624f), new (32.03879f, 36.60557f),
                new (32.29923f, 36.36939f), new (32.58526f, 36.42342f), new (32.876f, 36.53085f),
                new (33.42577f, 36.38619f), new (33.88485f, 35.21272f), new (34.62434f, 34.02968f),
                new (34.73527f, 33.69278f), new (34.86366f, 33.55389f), new (35.10379f, 33.08732f),
                new (35.35777f, 32.77784f), new (35.69171f, 32.50069f), new (35.84656f, 32.22465f),
                new (36.01643f, 32.11908f), new (36.17846f, 31.92439f), new (36.32175f, 31.51735f),
                new (36.49083f, 31.40269f), new (36.6428f, 31.09395f), new (36.98143f, 30.87008f),
                new (37.34995f, 30.98518f), new (37.65298f, 30.35742f), new (38.14125f, 29.79839f),
                new (38.30097f, 29.57764f), new (38.63807f, 29.33636f), new (38.79191f, 29.04884f),
                new (38.95393f, 28.85409f), new (39.28638f, 28.56006f), new (39.44593f, 28.33743f),
                new (39.59904f, 28.04167f), new (39.76351f, 27.87474f), new (40.23344f, 27.10765f),
                new (40.5593f, 26.7389f), new (40.64825f, 26.77288f), new (40.15792f, 28.3197f),
                new (40.47878f, 27.74801f), new (40.6029f, 27.9564f), new (40.46587f, 28.58052f),
                new (40.49842f, 29.02806f), new (40.85061f, 29.12524f), new (41.09647f, 28.99164f),
                new (41.32619f, 28.90017f), new (41.62305f, 29.14183f), new (41.9069f, 29.41749f),
                new (42.3455f, 29.289f), new (42.62182f, 29.07582f), new (42.94831f, 28.73164f),
                new (43.10726f, 28.825f), new (43.09475f, 29.11192f), new (43.03548f, 29.52093f),
                new (43.47666f, 28.87722f), new (43.68863f, 28.83212f), new (44.24405f, 30.16822f),
                new (44.15243f, 30.4401f), new (44.13583f, 30.68668f), new (43.97319f, 31.2235f),
                new (43.59188f, 32.07505f), new (43.55514f, 32.32842f), new (43.28082f, 32.9029f),
                new (43.19778f, 33.17189f), new (42.99816f, 33.4802f), new (42.90062f, 33.75408f),
                new (42.84206f, 34.01482f), new (42.60212f, 34.33672f), new (42.51172f, 34.60819f),
                new (42.49178f, 34.8559f), new (42.44653f, 35.11214f), new (42.40163f, 35.85024f),
                new (42.3139f, 36.1208f), new (42.2475f, 36.86615f), new (42.09885f, 37.39825f),
                new (41.93857f, 37.69329f), new (41.9395f, 38.41592f), new (41.74668f, 39.06109f),
                new (41.71388f, 39.41131f), new (41.4817f, 39.82877f), new (41.40123f, 40.19506f),
                new (41.83162f, 40.72823f), new (41.72264f, 41.10414f), new (41.74435f, 41.43597f),
                new (41.68886f, 41.79384f), new (41.49154f, 42.19954f), new (41.38077f, 42.57606f),
                new (41.07294f, 43.35818f), new (40.84647f, 43.77372f), new (40.94663f, 44.0791f),
                new (40.84524f, 44.45245f), new (39.73372f, 45.59874f), new (39.7615f, 45.89402f),
                new (39.86456f, 46.3304f), new (39.79302f, 46.43954f), new (39.95979f, 46.8737f),
                new (40.02057f, 47.10924f), new (39.68147f, 46.96014f), new (39.87912f, 47.57381f),
                new (39.88906f, 47.83566f), new (39.62033f, 47.85281f), new (39.28307f, 47.74151f),
                new (38.99869f, 47.72932f), new (38.72726f, 47.7414f), new (37.57701f, 47.14798f),
                new (37.24718f, 47.0506f), new (37.07909f, 47.25636f), new (36.70338f, 47.07301f),
                new (36.64832f, 47.4906f), new (36.43682f, 47.61499f), new (36.1853f, 47.66438f),
                new (35.82184f, 47.50399f), new (35.3458f, 47.65338f), new (35.14989f, 47.98354f),
                new (35.08235f, 48.16993f), new (34.95149f, 48.71061f), new (34.80748f, 49.13337f),
                new (35.23618f, 45.39426f), new (35.2403f, 45.17966f), new (34.40559f, 48.73275f),
                new (34.30258f, 48.75045f), new (34.44608f, 47.66825f), new (34.40862f, 47.59851f),
                new (34.21555f, 47.40396f), new (33.79879f, 47.39106f), new (33.84159f, 47.00494f),
                new (33.46966f, 46.95563f), new (33.17641f, 47.0181f), new (32.9339f, 47.03938f),
                new (32.56659f, 46.98632f), new (31.7903f, 47.26541f), new (31.56507f, 47.27264f),
                new (31.26646f, 47.33947f), new (31.65442f, 46.67304f), new (31.69027f, 46.29256f),
                new (32.24787f, 45.48835f), new (32.47871f, 44.59815f), new (32.53637f, 44.19995f),
                new (32.54522f, 43.84141f), new (32.44212f, 43.57378f), new (32.21579f, 43.47334f),
                new (31.94443f, 43.40946f), new (31.53552f, 43.17303f), new (30.83317f, 43.17491f),
                new (30.61916f, 43.06446f), new (30.34215f, 43.00517f), new (30.04128f, 42.681f),
                new (30.19507f, 41.98758f), new (29.75365f, 41.77756f), new (29.58416f, 41.34669f),
                new (29.70014f, 40.94119f), new (29.99722f, 40.84808f), new (30.21854f, 40.72022f),
                new (29.90438f, 40.11747f), new (29.82732f, 39.62344f), new (30.09426f, 39.28717f),
                new (30.51098f, 39.01957f), new (30.38978f, 38.73465f), new (30.38398f, 38.04395f),
                new (30.40719f, 37.85884f), new (30.80989f, 37.99457f), new (31.34938f, 38.09515f)
            },
            constraints: new[]
            {
                0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13, 14, 14,
                15, 15, 16, 16, 17, 17, 18, 18, 19, 19, 20, 20, 21, 21, 22, 22, 23, 23, 24, 24, 25, 25, 26, 26,
                27, 27, 28, 28, 29, 29, 30, 30, 31, 31, 32, 32, 33, 33, 34, 34, 35, 35, 36, 36, 37, 37, 38, 38,
                39, 39, 40, 40, 41, 41, 42, 42, 43, 43, 44, 44, 45, 45, 46, 46, 47, 47, 48, 48, 49, 49, 50, 50,
                51, 51, 52, 52, 53, 53, 54, 54, 55, 55, 56, 56, 57, 57, 58, 58, 59, 59, 60, 60, 61, 61, 62, 62,
                63, 63, 64, 64, 65, 65, 66, 66, 67, 67, 68, 68, 69, 69, 70, 70, 71, 71, 72, 72, 73, 73, 74, 74,
                75, 75, 76, 76, 77, 77, 78, 78, 79, 79, 80, 80, 81, 81, 82, 82, 83, 83, 84, 84, 85, 85, 86, 86,
                87, 87, 88, 88, 89, 89, 90, 90, 91, 91, 92, 92, 93, 93, 94, 94, 95, 95, 96, 96, 97, 97, 98, 98,
                99, 99, 100, 100, 101, 101, 102, 102, 103, 103, 104, 104, 105, 105, 106, 106, 107, 107, 108, 108,
                109, 109, 110, 110, 111, 111, 112, 112, 113, 113, 114, 114, 115, 115, 116, 116, 117, 117, 118, 118,
                119, 119, 120, 120, 121, 121, 122, 122, 123, 123, 124, 124, 125, 125, 126, 126, 127, 127, 128, 128,
                129, 129, 130, 130, 131, 131, 132, 132, 133, 133, 134, 134, 135, 135, 136, 136, 137, 137, 138, 138,
                139, 139, 140, 140, 141, 141, 142, 142, 143, 143, 144, 144, 145, 145, 146, 146, 147, 147, 148, 148,
                149, 149, 150, 150, 151, 151, 152, 152, 153, 153, 154, 154, 155, 155, 156, 156, 157, 157, 158, 158, 0
            }
        );

        public static readonly (double2[] points, int[] constraints, double2[] holes) Issue111 = (
            points: new double2[]
            {
                new(16.5f,1.5f),
                new(16.5f,2.5f),
                new(7.5f,2.5f),
                new(7.5f,8.5f),
                new(0.5f,8.5f),
                new(0.5f,15.5f),
                new(7.5f,15.5f),
                new(7.5f,20.5f),
                new(16.5f,20.5f),
                new(16.5f,21.5f),
                new(24.5f,21.5f),
                new(24.5f,20.5f),
                new(33.5f,20.5f),
                new(33.5f,15.5f),
                new(39.5f,15.5f),
                new(39.5f,8.5f),
                new(33.5f,8.5f),
                new(33.5f,2.5f),
                new(24.5f,2.5f),
                new(24.5f,1.5f),
                new(15.5f,15.5f),
                new(25.5f,15.5f),
                new(25.5f,18.5f),
                new(15.5f,18.5f),
                new(15.5f,4.5f),
                new(25.5f,4.5f),
                new(25.5f,7.5f),
                new(15.5f,7.5f),
                new(10.5f,6.5f),
                new(12.5f,6.5f),
                new(12.5f,17.5f),
                new(10.5f,17.5f),
                new(28.5f,6.5f),
                new(30.5f,6.5f),
                new(30.5f,17.5f),
                new(28.5f,17.5f),
                new(0.5f,11.5f),
                new(20.5f,21.5f),
                new(39.5f,11.5f),
                new(20.5f,1.44f),
            },
            constraints: new[]
            {
                0,1, 1,2, 2,3, 3,4, 36,5, 5,6, 6,7, 7,8, 8,9, 37,10, 10,11, 11,12, 12,13, 13,14, 38,15, 15,16, 16,17, 17,18, 18,19, 39,0, 20,21, 21,22, 22,23, 23,20, 24,25, 25,26, 26,27, 27,24, 28,29, 29,30, 30,31, 31,28, 32,33, 33,34, 34,35, 35,32, 4,36, 9,37, 14,38, 19,39
            },
            holes: new double2[]
            {
                new(16.5f,16.5f),
                new(16.5f,5.5f),
                new(11.5f,7.5f),
                new(29.5f,7.5f),
            }
        );
    }
}