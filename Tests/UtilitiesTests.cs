using NUnit.Framework;
using System;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.Collections;
using Unity.Collections.NotBurstCompatible;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;
using static andywiecko.BurstTriangulator.Utilities;

namespace andywiecko.BurstTriangulator.Editor.Tests
{
    public class UtilitiesTests
    {
        private static readonly TestCaseData[] generateHalfedgesTestData =
        {
            new(new int[]{ })
            {
                TestName = "Test case 0: zero triangles (GenerateHalfedgesTest)",
                ExpectedResult = new int[]{ }
            },
            new(new int[]{ 0, 1, 2})
            {
                TestName = "Test case 1: one triangle (GenerateHalfedgesTest)",
                ExpectedResult = new int[]{ -1, -1, -1 }
            },
            new(new int[]{ 0, 1, 2, 3, 4, 5 })
            {
                TestName = "Test case 2a: two triangles (GenerateHalfedgesTest)",
                ExpectedResult = new int[]{ -1, -1, -1, -1, -1, -1 }
            },
            new(new int[]{ 0, 1, 2, 2, 1, 3 })
            {
                TestName = "Test case 2b: two triangles (GenerateHalfedgesTest)",
                ExpectedResult = new int[]{ -1, 3, -1, 1, -1, -1 }
            },
            new(new int[]{ 94,93,95,95,92,96,99,130,121,93,92,95,121,103,102,470,89,88,100,121,101,102,101,121,294,91,90,130,97,131,100,99,121,482,477,82,103,121,104,130,129,121,105,104,120,98,130,99,77,76,78,483,482,79,482,81,80,119,106,105,98,97,130,482,80,79,121,120,104,119,107,106,119,105,120,79,484,483,97,96,131,121,128,122,129,128,121,131,96,92,123,127,126,128,127,122,118,108,107,127,123,122,132,131,92,118,107,119,124,123,126,118,117,108,91,132,92,485,79,78,117,109,108,91,133,132,126,125,124,117,116,109,486,485,76,116,110,109,91,134,133,116,115,110,115,111,110,485,78,76,485,484,79,91,135,134,115,114,111,114,112,111,91,136,135,114,113,112,84,476,85,85,474,86,86,474,87,87,470,88,89,470,90,83,477,84,82,477,83,482,82,81,76,75,486,75,74,486,66,70,69,477,476,84,476,475,85,475,474,85,139,138,136,136,138,137,68,67,69,486,74,73,482,478,477,72,486,73,482,479,478,474,471,87,473,472,474,480,479,482,67,66,69,145,143,142,472,471,474,481,480,482,72,71,486,471,470,87,486,71,70,355,469,468,140,145,142,66,65,70,356,355,467,356,467,466,464,463,465,65,486,70,356,466,357,466,465,357,142,141,140,65,487,486,455,389,388,463,462,357,455,390,389,140,146,145,387,455,388,461,377,462,140,139,146,487,390,455,145,144,143,391,390,487,391,487,65,378,461,460,375,358,357,462,376,357,465,463,357,467,355,468,469,329,470,459,379,460,146,139,147,459,458,380,354,330,355,60,55,61,355,330,469,470,294,90,330,329,469,147,139,136,380,458,457,353,330,354,329,326,470,381,457,382,381,380,457,457,456,382,380,379,459,382,456,383,148,147,162,148,159,153,353,331,330,352,331,353,59,58,60,379,378,460,358,360,359,328,327,329,383,456,384,327,326,329,361,368,367,384,456,385,378,377,461,149,152,150,325,324,326,326,324,470,351,331,352,351,332,331,385,456,455,377,376,462,58,55,60,386,385,455,350,333,332,361,360,368,324,504,470,376,375,357,149,148,152,387,386,455,348,334,333,363,362,365,350,332,351,392,65,393,55,62,61,323,504,324,364,363,365,375,374,358,349,333,350,347,335,334,55,58,57,152,148,153,56,55,57,65,392,391,365,362,366,322,504,323,346,336,335,148,160,159,374,373,358,349,348,333,366,362,361,367,366,361,64,393,65,152,151,150,368,360,358,346,337,336,162,161,160,321,504,322,369,368,358,348,347,334,371,369,358,505,504,320,320,504,321,371,358,373,372,371,373,64,63,393,63,62,393,371,370,369,319,505,320,345,338,337,346,335,347,157,154,153,148,162,160,488,505,319,393,62,55,318,488,319,344,339,338,346,345,337,394,393,55,156,155,154,394,55,453,498,470,504,159,158,153,158,157,153,501,500,504,157,156,154,501,504,503,502,501,503,489,488,318,317,489,318,147,136,162,500,499,504,499,498,504,345,344,338,395,394,453,395,453,396,341,340,339,162,164,163,498,497,470,162,136,164,317,490,489,316,490,317,396,453,397,316,314,490,164,136,91,344,341,339,343,341,344,398,400,399,492,304,493,309,491,490,165,164,91,314,316,315,301,495,302,343,342,341,496,495,300,295,294,497,294,470,497,264,508,91,397,453,398,507,165,91,497,496,299,295,497,296,508,507,91,453,400,398,296,497,297,294,293,91,297,497,298,497,299,298,261,509,508,453,55,54,496,300,299,167,166,165,168,165,507,495,301,300,265,264,91,494,302,495,314,309,490,50,48,51,494,303,302,494,493,303,53,451,454,303,493,304,260,510,509,168,167,165,265,91,266,304,492,305,266,91,267,305,492,491,264,263,508,306,305,491,267,91,268,313,309,314,49,48,50,292,288,293,263,262,508,291,289,292,506,168,507,289,288,292,268,91,269,262,261,508,453,53,454,269,91,293,312,310,313,258,511,510,310,309,313,308,306,491,288,269,293,169,168,506,309,308,491,261,260,509,48,47,51,271,270,269,291,290,289,271,269,288,312,311,310,453,54,53,272,271,273,308,307,306,53,52,451,170,169,506,260,259,510,273,271,288,517,170,506,258,512,511,451,52,51,453,401,400,283,273,288,402,401,453,259,258,510,287,283,288,286,283,287,171,170,517,47,451,51,283,274,273,514,513,257,172,171,517,449,451,441,283,275,274,448,452,451,276,275,283,403,402,453,286,284,283,285,284,286,277,276,283,172,174,173,449,448,451,257,513,512,172,175,174,257,512,258,282,277,283,404,403,453,282,278,277,279,278,282,280,279,282,451,47,441,405,404,453,176,175,172,441,47,46,281,280,282,405,453,406,441,46,45,448,447,452,44,441,45,406,408,407,41,441,44,441,450,449,441,440,450,406,453,408,43,41,44,439,446,440,440,446,450,453,409,408,39,442,441,257,256,514,514,242,515,42,41,43,453,410,409,452,447,22,444,443,442,439,24,446,38,444,442,23,447,446,39,441,41,25,438,437,256,255,514,40,39,41,517,176,172,438,24,439,25,24,438,24,23,446,410,453,411,39,38,442,435,445,444,517,177,176,26,437,436,23,22,447,243,242,514,38,37,444,411,453,452,437,26,25,517,516,177,37,435,444,27,436,28,412,411,452,37,36,435,436,27,26,412,452,22,177,240,178,29,436,435,243,514,255,413,412,22,413,22,21,177,516,240,36,29,435,35,29,36,29,28,436,414,413,21,242,516,515,415,414,21,35,30,29,35,34,30,243,255,244,415,21,416,244,255,254,242,241,516,223,179,178,416,21,20,34,31,30,34,33,31,253,244,254,245,244,253,416,20,417,241,240,516,33,32,31,417,20,418,181,180,179,246,245,248,20,419,418,240,224,178,248,245,253,252,248,253,17,14,18,20,19,419,223,181,179,225,224,227,248,247,246,250,249,252,252,249,248,183,185,184,419,19,420,193,186,202,16,14,17,224,240,227,223,182,181,183,182,185,251,250,252,190,187,186,14,19,18,190,188,187,15,14,16,190,189,188,240,228,227,421,420,19,239,228,240,191,190,192,224,223,178,236,234,237,234,238,237,235,234,236,228,239,238,227,226,225,234,228,238,14,421,19,422,421,14,234,229,228,223,221,182,234,230,229,233,230,234,423,422,14,423,14,13,190,193,192,233,231,230,232,231,233,222,221,223,12,11,13,424,423,13,190,186,193,221,220,182,425,424,13,11,10,13,186,185,202,185,182,202,10,425,13,219,218,220,211,182,220,218,211,220,10,426,425,211,210,182,9,426,10,210,209,182,218,212,211,201,194,193,217,212,218,209,202,182,9,427,426,203,202,209,202,201,193,9,428,427,204,203,209,204,209,208,201,195,194,200,195,201,205,204,208,217,216,212,216,213,212,206,208,207,8,428,9,206,205,208,199,196,195,199,195,200,8,429,428,216,214,213,215,214,216,7,429,8,197,199,198,7,430,429,199,197,196,6,430,7,6,431,430,5,431,6,5,4,431,4,432,431,2,433,432,2,434,433,2,432,4,3,2,4,2,1,434,1,0,434 })
            {
                TestName = "Test case 3: many triangles (GenerateHalfedgesTest)",
                ExpectedResult = new int[]{ -1, 11, -1, 10, 88, -1, 46, 41, 31, -1, 3, 1, 36, -1, 23, 180, -1, 178, 32, 22, -1, -1, 19, 14, 767, -1, 364, 61, 80, -1, -1, 8, 18, 224, 186, 189, 12, 68, -1, -1, 86, 7, -1, 67, 73, 62, 6, -1, -1, 148, -1, -1, 65, 77, 191, -1, 63, 71, -1, 72, -1, 27, 45, 56, -1, 52, -1, 43, 37, 106, -1, 57, 59, 44, -1, 151, -1, 53, -1, 87, 28, 85, 95, -1, -1, 81, 40, 79, 4, 103, 99, -1, 109, -1, 101, 82, 113, -1, 105, 90, -1, 94, -1, 89, 115, 98, 69, -1, -1, 92, 128, -1, 122, 96, 125, 104, -1, 152, -1, 147, 131, -1, 112, 140, -1, 114, -1, -1, 110, -1, 137, 120, -1, 149, 194, 143, -1, 130, 155, -1, 123, -1, 146, 135, 158, -1, 142, 119, 49, 133, -1, 75, 117, 164, -1, 138, -1, 161, 144, 167, -1, 157, 703, -1, 153, -1, -1, 159, 202, 206, -1, 208, 174, -1, 172, 233, -1, 256, 17, -1, 15, 365, -1, 187, 203, -1, 34, 183, -1, 35, -1, 54, -1, 197, 134, -1, 219, 193, 269, -1, 241, -1, 168, 184, -1, 209, 169, -1, 171, 205, -1, 213, 370, 211, -1, -1, -1, 242, -1, 196, -1, 226, 230, -1, 33, 254, 221, -1, 238, -1, 222, 247, 257, 175, -1, 248, -1, -1, 228, 250, -1, 200, 217, 320, -1, 265, -1, 231, 235, -1, 239, -1, -1, 258, 225, -1, 177, 232, 253, -1, 280, 362, -1, 340, 305, 245, 290, -1, 281, 198, -1, 339, 273, 272, -1, 282, -1, 336, -1, 293, 260, 268, 275, 287, -1, -1, 338, 283, -1, -1, 266, 325, -1, 279, 302, -1, 307, -1, 335, 337, 316, -1, 294, 314, -1, 264, 479, 296, -1, 433, 455, -1, -1, 348, 303, 322, 300, -1, -1, -1, 243, -1, 315, 324, 323, 291, 521, 434, -1, 412, 503, -1, 472, 454, 473, 298, 277, 299, 286, 271, 263, -1, 367, 380, -1, 391, 413, -1, 313, 369, -1, -1, 372, 392, 376, 360, -1, 457, 494, -1, 355, 368, 261, 738, 26, 181, -1, 342, 361, 349, 212, 660, 352, -1, 385, 404, 354, -1, 424, 443, 343, 386, 389, -1, -1, 374, 381, -1, 393, 382, -1, 345, 353, 388, 420, -1, -1, 662, 606, 533, 638, 514, 406, -1, 375, 445, 402, -1, -1, 458, -1, -1, 329, 346, 553, -1, -1, -1, 425, -1, 394, 429, -1, -1, 378, 418, 467, -1, 545, 421, 450, -1, -1, 309, 327, 476, 551, -1, -1, 441, -1, 439, 470, 379, 449, 405, -1, 487, -1, 444, 430, -1, 460, -1, 333, 310, 510, 357, 409, -1, 452, 478, 505, -1, 486, -1, 552, 426, 496, 634, 442, -1, 332, 334, -1, 513, 435, -1, 461, 306, 569, -1, 538, -1, 522, 499, 464, 447, -1, 519, 547, -1, 613, -1, 358, 526, 468, -1, -1, 485, -1, -1, 536, 330, 539, 462, -1, 601, -1, 568, 456, -1, 517, 475, 401, -1, -1, 512, -1, 489, -1, 326, 484, 540, -1, 562, 495, -1, 557, -1, 600, 608, -1, 399, -1, 580, 502, -1, 482, 504, 523, -1, 544, -1, 542, 428, 587, 490, -1, -1, -1, 436, 466, 414, 565, 623, -1, 528, -1, -1, 607, 577, 525, -1, -1, 554, 571, -1, 509, 480, 593, 566, 579, -1, 576, 595, 574, 561, -1, 572, 535, 583, -1, 581, -1, -1, 590, 546, -1, 612, 586, -1, -1, 570, 610, 575, -1, 671, -1, 622, 530, 507, -1, 647, -1, 640, 398, 560, 531, -1, 594, 616, 589, 492, 625, 655, 611, -1, 707, -1, 670, -1, 599, 555, -1, 614, 630, -1, -1, 646, 626, 777, 673, 686, 469, 667, -1, 641, 400, -1, 605, 637, -1, 665, 648, -1, 629, 603, 644, -1, 652, -1, 650, -1, -1, 615, 658, 692, 656, -1, 371, 687, 397, -1, 668, 643, -1, 635, 664, -1, 620, 597, -1, 632, 675, 674, 696, -1, -1, -1, 706, 689, -1, -1, -1, 739, 633, 661, 702, 681, 694, -1, 657, 701, 690, -1, 676, 744, -1, 723, 800, 693, 688, 162, 721, 709, 680, 618, 731, 705, -1, 760, -1, -1, 825, 814, -1, 896, -1, 799, -1, 704, 748, 699, -1, -1, 789, 796, -1, -1, -1, 708, -1, 791, 780, -1, 740, 753, 363, 685, 736, 836, 758, 793, 697, 761, -1, 787, 722, 757, -1, 782, 771, 737, 762, -1, -1, 749, 742, 947, 711, 745, 754, 768, -1, -1, 874, 24, 763, 773, -1, 752, -1, 769, 899, -1, 868, 631, -1, 915, 734, -1, 751, -1, -1, 820, 821, 747, 859, 726, -1, 733, -1, 743, 822, 806, 727, -1, 844, 719, 700, 847, 902, -1, 809, -1, 795, -1, 813, 804, 926, -1, 871, 808, 715, -1, 932, -1, 898, -1, 785, 786, 794, 828, -1, 714, 831, -1, 823, 840, -1, 826, -1, 838, -1, 854, 741, -1, 833, 886, 829, 864, -1, 883, 798, -1, -1, 801, -1, 862, 890, -1, -1, 869, 835, 908, 863, -1, 892, 788, -1, -1, 849, 856, 841, 873, -1, -1, 776, 853, 917, 812, -1, 865, 766, 889, 914, 884, -1, 941, -1, 955, -1, 843, 877, 923, 839, 895, 910, 875, 850, -1, 858, 928, -1, 887, 717, -1, 818, 774, -1, 968, 802, -1, -1, 909, -1, -1, 855, 905, 888, 934, -1, -1, 876, 779, -1, 870, -1, 933, -1, -1, -1, 885, -1, 942, 810, -1, 893, 937, -1, 956, 816, 919, 911, 949, 964, 929, -1, 1015, -1, 879, 925, -1, 967, 952, -1, 759, 971, 935, 958, -1, 945, 991, -1, 881, 931, 961, 950, -1, 995, 957, -1, -1, 936, 976, 1032, 944, 901, 983, -1, 948, -1, 1008, 1091, -1, 965, 1130, 1007, 1034, 1067, 988, -1, 969, 1055, -1, 1006, -1, 981, 1000, -1, 953, 1021, 997, -1, 960, -1, 993, -1, -1, 989, 1018, 1013, -1, -1, -1, 986, 978, 973, -1, 1014, 1039, -1, 1002, 1010, 939, -1, 1025, 1001, -1, -1, 992, 1036, 1027, -1, 1017, -1, 1023, 1030, -1, 1028, 1045, 966, 1041, 979, -1, 1022, 1047, -1, 1011, 1129, 1033, -1, 1050, -1, 1031, -1, 1037, 1071, -1, 1043, -1, 1057, -1, 1101, 984, 1063, 1052, -1, 1073, -1, -1, 1117, 1056, 1075, 1070, -1, 980, -1, 1082, 1065, 1048, 1085, 1059, 1096, 1064, -1, 1109, 1080, -1, 1078, -1, 1069, 1100, -1, 1072, 1145, -1, 1116, -1, 1124, 974, 1159, 1223, -1, -1, 1074, -1, 1140, -1, 1083, 1054, 1156, 1189, -1, -1, 1111, 1132, 1139, 1077, 1163, 1106, 1144, 1157, -1, 1138, 1088, 1062, 1126, 1136, -1, 1169, -1, 1198, 1090, -1, 1118, -1, 1151, 1040, 977, 1135, 1107, -1, -1, 1131, 1119, -1, 1115, 1108, 1098, 1164, -1, -1, 1112, 1086, -1, -1, 1174, 1172, -1, 1128, 1167, -1, 1187, -1, 1102, 1113, -1, 1092, 1197, -1, 1175, 1110, 1141, -1, 1180, 1152, -1, 1121, -1, 1206, 1149, 1184, 1148, 1162, 1185, 1216, -1, -1, 1166, 1188, -1, 1211, 1173, 1176, -1, 1154, 1181, 1103, 1201, 1208, 1286, -1, 1217, -1, 1210, 1160, 1123, 1233, -1, 1190, 1203, 1202, -1, 1219, 1171, 1267, 1191, 1213, 1196, 1183, 1229, 1209, -1, -1, 1177, 1194, -1, 1205, 1225, 1244, -1, 1093, -1, 1220, 1236, 1232, -1, 1212, -1, 1253, 1227, 1199, 1239, -1, 1226, 1248, -1, 1234, -1, 1258, -1, 1268, 1221, 1301, -1, 1366, 1237, -1, 1263, 1256, -1, 1231, -1, 1271, 1251, 1261, 1241, -1, -1, 1257, 1288, 1250, 1272, -1, -1, 1207, 1243, -1, -1, 1255, 1264, 1283, -1, -1, -1, 1300, -1, 1287, 1307, 1298, -1, 1273, 1326, 1367, 1192, 1279, 1262, 1291, 1313, 1289, -1, 1324, 1343, -1, -1, 1317, 1281, 1331, 1277, 1245, -1, 1328, 1382, -1, -1, 1280, -1, 1311, 1336, 1309, -1, 1290, 1334, -1, -1, 1297, 1357, -1, 1429, 1442, 1490, 1348, 1293, -1, 1284, 1355, 1303, 1397, -1, 1299, -1, 1443, 1314, -1, 1310, -1, 1346, -1, 1428, 1388, -1, 1294, 1352, -1, 1338, -1, 1323, -1, -1, -1, 1344, 1360, -1, 1327, -1, 1318, 1387, 1377, 1353, -1, -1, 1412, -1, -1, 1247, 1285, 1375, 1373, -1, 1385, -1, 1369, -1, 1368, -1, 1359, -1, 1384, -1, -1, 1304, 1394, 1379, 1371, 1390, 1358, 1341, -1, 1386, 1405, 1400, -1, 1383, 1420, 1433, 1329, 1402, -1, 1392, 1415, 1398, -1, -1, 1391, 1407, 1406, -1, 1426, 1430, -1, 1363, 1417, -1, 1401, -1, 1413, -1, -1, 1395, -1, -1, 1439, -1, -1, 1409, 1435, 1340, 1320, 1410, -1, 1453, 1396, -1, 1427, 1447, -1, 1448, 1423, -1, 1445, 1321, 1333, 1480, 1441, 1460, 1436, 1438, -1, 1457, -1, 1463, 1432, 1456, 1472, 1454, 1450, 1465, -1, 1446, -1, 1469, 1452, 1484, 1458, -1, -1, 1481, 1462, 1477, -1, 1455, 1502, -1, 1489, 1511, 1470, -1, 1486, 1444, 1468, 1493, -1, 1464, -1, 1479, 1495, -1, 1475, 1322, 1519, -1, 1482, -1, 1487, 1497, 1496, -1, 1507, 1504, -1, 1473, 1528, 1500, -1, -1, 1499, 1522, -1, 1514, 1476, 1535, -1, 1510, 1523, -1, -1, 1532, 1491, -1, -1, 1508, 1515, 1550, -1, 1527, 1526, 1503, -1, 1540, -1, 1518, 1537, -1, 1512, -1, 1533, -1, 1547, 1530, -1, 1548, -1, -1, 1552, -1, 1539, 1542, -1, 1524, 1556, 1545, -1, 1558, -1, 1551, 1562, 1554, -1, -1, 1565, 1557, 1573, -1, 1561, 1571, -1, 1572, 1580, -1, 1566, 1568, 1563, 1576, -1, 1574, -1, -1, 1583, 1569, -1, -1, 1579 }
            },
        };

        [Test, TestCaseSource(nameof(generateHalfedgesTestData))]
        public int[] GenerateHalfedgesTest(int[] triangles)
        {
            var halfedges = new int[triangles.Length];
            GenerateHalfedges(halfedges, triangles, Allocator.Persistent);
            return halfedges;
        }

        [Test]
        public void GenerateHalfedgesThrowTest() => Assert.Throws<ArgumentException>(() =>
            GenerateHalfedges(halfedges: new int[4], triangles: new int[7], Allocator.Persistent)
        );

        private static readonly TestCaseData[] generatePointTriangleCountTestData =
        {
            new(new int[0])
            {
                TestName = $"Test case 1 ({nameof(GeneratePointTriangleCountTest)})",
                ExpectedResult = new int[0],
            },
            new(new int[] { 0, 1, 2 })
            {
                TestName = $"Test case 2 ({nameof(GeneratePointTriangleCountTest)})",
                ExpectedResult = new int[] { 1, 1, 1 },
            },
            new(new int[] { 0, 1, 2, 0, 1, 2 })
            {
                TestName = $"Test case 3 ({nameof(GeneratePointTriangleCountTest)})",
                ExpectedResult = new int[] { 2, 2, 2 },
            },
            new(new int[] { 1, 1, 1, 1, 1, 1 })
            {
                TestName = $"Test case 4 ({nameof(GeneratePointTriangleCountTest)})",
                ExpectedResult = new int[] { 0, 6 },
            },
        };

        [Test, TestCaseSource(nameof(generatePointTriangleCountTestData))]
        public int[] GeneratePointTriangleCountTest(int[] triangles)
        {
            var max = -1;
            foreach (var t in triangles)
            {
                max = math.max(t, max);
            }
            var pointTriangleCount = new int[max >= 0 ? max + 1 : 0];
            GeneratePointTriangleCount(pointTriangleCount, triangles);
            return pointTriangleCount;
        }

        [Test]
        public void GeneratePointTriangleCountThrowTest() => Assert.Throws<ArgumentException>(() =>
            GeneratePointTriangleCount(new int[5], triangles: new int[] { 0, 1, 2, 3, 4, 5 })
        );

        private static readonly TestCaseData[] nextHalfedgeTestData =
        {
            new(0) { ExpectedResult = 1, TestName= "Test case 1 (NextHalfedgeTest)"},
            new(1) { ExpectedResult = 2, TestName= "Test case 2 (NextHalfedgeTest)"},
            new(2) { ExpectedResult = 0, TestName= "Test case 3 (NextHalfedgeTest)"},
            new(3) { ExpectedResult = 4, TestName= "Test case 4 (NextHalfedgeTest)"},
            new(4) { ExpectedResult = 5, TestName= "Test case 5 (NextHalfedgeTest)"},
            new(5) { ExpectedResult = 3, TestName= "Test case 6 (NextHalfedgeTest)"},
        };

        [Test, TestCaseSource(nameof(nextHalfedgeTestData))]
        public int NextHalfedgeTest(int he) => NextHalfedge(he);

        private static readonly TestCaseData[] insertSubMeshTestData =
        {
            new(
                (new float2[]{ }, new int[]{ }),
                (new float2[]{ }, new int[]{ })
            )
            {
                ExpectedResult = new int[]{ },
                TestName = "Test case 1 - identity (InsertSubMeshTest)"
            },
            new(
                (new float2[]{ }, new int[]{ }),
                (new float2[]{ default, default, default }, new int[]{ 0, 1, 2 })
            )
            {
                ExpectedResult = new int[]{ 0, 1, 2 },
                TestName = "Test case 2 - empty mesh (InsertSubMeshTest)"
            },
            new(
                (new float2[]{ 1, 2, 3 }, new int[]{ 0, 1, 2 }),
                (new float2[]{ 4, 5, 6 }, new int[]{ 0, 1, 2 })
            )
            {
                ExpectedResult = new int[]{ 0, 1, 2, 3, 4, 5 },
                TestName = "Test case 3 (InsertSubMeshTest)"
            },
            new(
                (new float2[]{ 1, 2, 3, 4 }, new int[]{ 0, 1, 2, 2, 1, 3 }),
                (new float2[]{ 4, 5, 6, 7, 8, 9 }, new int[]{ 3, 4, 5, 2, 1, 0 })
            )
            {
                ExpectedResult = new int[]{ 0, 1, 2, 2, 1, 3, 7, 8, 9, 6, 5, 4 },
                TestName = "Test case 4 (InsertSubMeshTest)"
            },
        };

        [Test, TestCaseSource(nameof(insertSubMeshTestData))]
        public int[] InsertSubMeshTest((float2[] p, int[] t) mesh, (float2[] p, int[] t) subMesh)
        {
            using var positions = new NativeList<float2>(Allocator.Persistent);
            using var triangles = new NativeList<int>(Allocator.Persistent);
            positions.CopyFromNBC(mesh.p);
            triangles.CopyFromNBC(mesh.t);

            InsertSubMesh(positions, triangles, subMesh.p, subMesh.t);

            // NOTE:
            //   NUnit does not support for returning type of (float2[], int[]).
            Assert.That(positions.AsArray(), Is.EqualTo(mesh.p.Concat(subMesh.p)));
            return triangles.AsReadOnly().ToArray();
        }

        private static readonly TestCaseData[] generateTriangleColorsTestData =
        {
            new(new int[]{ }, 0)
            {
                ExpectedResult = new int[]{ },
                TestName = "Test case 0 - 0 triangles 0 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ -1, -1, -1 }, 1)
            {
                ExpectedResult = new int[]{ 0 },
                TestName = "Test case 1 - 1 triangle 1 color (GenerateTriangleColorsTest)"
            },
            new(new int[]{ -1, -1, -1, -1, -1, -1 }, 2)
            {
                ExpectedResult = new int[]{ 0, 1 },
                TestName = "Test case 2a - 2 triangles 2 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ 3, -1, -1, 0, -1, -1 }, 1)
            {
                ExpectedResult = new int[]{ 0, 0 },
                TestName = "Test case 2b - 2 triangles 1 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ -1, -1, -1, -1, -1, -1, -1, -1, -1 }, 3)
            {
                ExpectedResult = new int[]{ 0, 1, 2 },
                TestName = "Test case 3a - 3 triangles 3 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ 3, -1, -1, 0, -1, -1, -1, -1, -1 }, 2)
            {
                ExpectedResult = new int[]{ 0, 0, 1 },
                TestName = "Test case 3b - 3 triangles 2 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ 6, -1, -1, -1, -1, -1, 0, -1, -1 }, 2)
            {
                ExpectedResult = new int[]{ 0, 1, 0 },
                TestName = "Test case 3c - 3 triangles 2 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ -1, -1, -1, 6, -1, -1, 3, -1, -1 }, 2)
            {
                ExpectedResult = new int[]{ 0, 1, 1 },
                TestName = "Test case 3d - 3 triangles 2 colors (GenerateTriangleColorsTest)"
            },
            new(new int[]{ 3, -1, -1, 0, 6, -1, 4, -1, -1 }, 1)
            {
                ExpectedResult = new int[]{ 0, 0, 0 },
                TestName = "Test case 3e - 3 triangles 1 color (GenerateTriangleColorsTest)"
            },
        };

        [Test, TestCaseSource(nameof(generateTriangleColorsTestData))]
        public int[] GenerateTriangleColorsTest(int[] halfedges, int expectedCount)
        {
            var colors = new int[halfedges.Length / 3];
            GenerateTriangleColors(colors, halfedges, out var count, Allocator.Persistent);
            Assert.That(count, Is.EqualTo(expectedCount));
            return colors;
        }

        [Test]
        public void GenerateTriangleColorsThrowTest() => Assert.Throws<ArgumentException>(() =>
            GenerateTriangleColors(colors: new int[1], halfedges: new int[30], out _, Allocator.Persistent)
        );

        [Test]
        public void RetriangulateThrowEmptyMeshTest() => Debug.Log(Assert.Throws<InvalidOperationException>(() =>
            new Mesh().Retriangulate()
        ));

        [Test]
        public void RetriangulateThrowUVChannelOutOfRangeMeshTest([Values(-1, 8, 16)] int uvChanelIndex) => Debug.Log(Assert.Throws<ArgumentException>(() =>
            new Mesh { vertices = new Vector3[] { } }.Retriangulate(uvChannelIndex: uvChanelIndex)
        ));

        [Test]
        public void RetriangulateThrowFailedTriangulationTest()
        {
            var initialVertices = new Vector3[] { math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0) };
            var initialTriangles = new int[] { 0, 1, 2, 0, 1, 3 };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = initialTriangles,
            };

            Assert.Throws<Exception>(() =>
            {
                LogAssert.Expect(LogType.Error, new Regex(".*"));
                mesh.Retriangulate();
            });
            Assert.That(mesh.vertices, Is.EqualTo(initialVertices));
            Assert.That(mesh.triangles, Is.EqualTo(initialTriangles));
        }

        [Test]
        public void RetriangulateEmptyVerticesTest()
        {
            var mesh = new Mesh { vertices = new Vector3[] { } };
            mesh.Retriangulate();
            Assert.That(mesh.vertices, Is.Empty);
        }

        [Test]
        public void RetriangulateEmptyTrianglesTest()
        {
            var mesh = new Mesh { vertices = new Vector3[] { default } };
            mesh.Retriangulate();
            Assert.That(mesh.vertices, Is.EqualTo(new Vector3[] { default }));
        }

        [Test]
        public void RetriangulateSingleColorTest()
        {
            var inputVertices = new Vector3[] { math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0) };
            var mesh = new Mesh
            {
                vertices = inputVertices,
                triangles = new int[] { 0, 1, 2 }
            };

            mesh.Retriangulate();

            Assert.That(mesh.vertices, Is.EqualTo(inputVertices));
            // NOTE: triangulator will produce clockwise-oriented triangles.
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 2, 1 }).Using(TrianglesComparer.Instance));
        }

        [Test]
        public void RetriangulateManyColorsTest()
        {
            var inputVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0),
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0),
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0),
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0),
            };
            var mesh = new Mesh
            {
                vertices = inputVertices,
                triangles = new int[]
                {
                    0, 1, 2,
                    3, 4, 5,
                    6, 7, 8,
                    9, 10, 11,
                }
            };

            mesh.Retriangulate();

            Assert.That(mesh.vertices, Is.EqualTo(inputVertices));
            // NOTE: triangulator will produce clockwise-oriented triangles.
            Assert.That(mesh.triangles, Is.EqualTo(new[] { 0, 2, 1, 4, 3, 5, 7, 6, 8, 10, 9, 11 }).Using(TrianglesComparer.Instance));
        }

        [Test]
        public void RetriangulateSettingsTest()
        {
            var mesh = new Mesh
            {
                vertices = new Vector3[] { math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0) },
                triangles = new int[] { 0, 3, 2, 0, 2, 1 }
            };

            mesh.Retriangulate(settings: new()
            {
                AutoHolesAndBoundary = true,
                RefineMesh = true,
                RefinementThresholds = { Area = 0.25f, Angle = 0 },
            });

            Assert.That(mesh.vertices, Has.Length.GreaterThan(4));
            Assert.That(mesh.triangles, Has.Length.GreaterThan(6));
            TestUtils.AssertValidTriangulation(mesh);
        }

        [Test]
        public void RetriangulateAxisTest([Values] Axis input, [Values] Axis output)
        {
            var p = new[] { math.float2(0, 0), math.float2(1, 0), math.float2(1, 1) };
            Vector3[] verticies(Axis axis) => p.Select(i =>
            {
                var x = math.float3(i, 0);
                return (Vector3)(axis switch
                {
                    Axis.XY => x.xyz,
                    Axis.XZ => x.xzy,
                    Axis.YX => x.yxz,
                    Axis.YZ => x.zxy,
                    Axis.ZX => x.yzx,
                    Axis.ZY => x.zyx,
                    _ => throw new NotImplementedException(),
                });
            }).ToArray();
            var mesh = new Mesh
            {
                vertices = verticies(input),
                triangles = new int[] { 0, 1, 2 }
            };

            mesh.Retriangulate(axisInput: input, axisOutput: output);

            Assert.That(mesh.vertices, Is.EqualTo(verticies(output)));
        }

        [Test]
        public void RetriangulateUVNoneTest()
        {
            var initialVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0)
            };
            var initialUV = new Vector2[]
            {
                math.float2(0, 0), math.float2(1, 0), math.float2(1, 1), math.float2(0, 1)
            };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                uv = initialUV
            };

            mesh.Retriangulate(
                uvMap: UVMap.None,
                settings: new()
                {
                    RefineMesh = true,
                    RefinementThresholds = { Area = 0.25f, Angle = 0 }
                }
            );

            Assert.That(mesh.vertices, Is.EqualTo(initialVertices.Concat(new Vector3[] { math.float3(1, 0.5f, 0), math.float3(0, 0.5f, 0) })));
            Assert.That(mesh.uv, Is.EqualTo(initialUV.Concat(new Vector2[] { math.float2(0), math.float2(0) })));
        }

        [Test]
        public void RetriangulateUVNotNoneTest([Values(UVMap.Planar, UVMap.Barycentric)] UVMap uvMap)
        {
            var initialVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0)
            };
            var initialUV = new Vector2[]
            {
                math.float2(0, 0), math.float2(1, 0), math.float2(1, 1), math.float2(0, 1)
            };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                uv = initialUV
            };

            mesh.Retriangulate(
                uvMap: uvMap,
                settings: new()
                {
                    RefineMesh = true,
                    RefinementThresholds = { Area = 0.25f, Angle = 0 }
                }
            );

            Assert.That(mesh.vertices, Is.EqualTo(initialVertices.Concat(new Vector3[] { math.float3(1, 0.5f, 0), math.float3(0, 0.5f, 0) })));
            Assert.That(mesh.uv, Is.EqualTo(initialUV.Concat(new Vector2[] { math.float2(1, 0.5f), math.float2(0, 0.5f) })));
        }

        [Test]
        public void RetriangulateGenerateInitialUVPlanarMapTest()
        {
            var initialVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0)
            };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                uv = new Vector2[4]
            };

            mesh.Retriangulate(
                generateInitialUVPlanarMap: true,
                uvMap: UVMap.None,
                settings: new()
                {
                    RefineMesh = true,
                    RefinementThresholds = { Area = 0.25f, Angle = 0 }
                }
            );

            Assert.That(mesh.vertices, Is.EqualTo(initialVertices.Concat(new Vector3[] { math.float3(1, 0.5f, 0), math.float3(0, 0.5f, 0) })));
            Assert.That(mesh.uv, Is.EqualTo(new Vector2[] { math.float2(0, 0), math.float2(1, 0), math.float2(1, 1), math.float2(0, 1), math.float2(0, 0), math.float2(0, 0) }));
        }

        [Test]
        public void RetriangulateInsertTriangleMidPointsTest()
        {
            var initialVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0)
            };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                uv = new Vector2[4]
            };

            mesh.Retriangulate(
                generateInitialUVPlanarMap: true,
                insertTriangleMidPoints: true,
                uvMap: UVMap.None
            );

            Assert.That(mesh.vertices, Is.EqualTo(initialVertices.Concat(new Vector3[] { new(2f / 3, 1f / 3, 0), new(1f / 3, 2f / 3, 0) })));
            Assert.That(mesh.uv, Is.EqualTo(new Vector2[] { math.float2(0, 0), math.float2(1, 0), math.float2(1, 1), math.float2(0, 1), math.float2(2f / 3, 1f / 3), math.float2(1f / 3, 2f / 3) }));
        }

        [Test]
        public void RetriangulateInsertEdgeMidPointsTest()
        {
            var initialVertices = new Vector3[]
            {
                math.float3(0, 0, 0), math.float3(1, 0, 0), math.float3(1, 1, 0), math.float3(0, 1, 0)
            };
            var mesh = new Mesh
            {
                vertices = initialVertices,
                triangles = new int[] { 0, 1, 2, 0, 2, 3 },
                uv = new Vector2[4]
            };

            mesh.Retriangulate(
                generateInitialUVPlanarMap: true,
                insertEdgeMidPoints: true,
                uvMap: UVMap.None
            );

            Assert.That(mesh.vertices, Is.EqualTo(initialVertices.Concat(new Vector3[] { new(0.5f, 0), new(1, 0.5f), new(0.5f, 0.5f), new(0.5f, 1), new(0, 0.5f) })));
            Assert.That(mesh.uv, Is.EqualTo(new Vector2[] { new(0, 0), new(1, 0), new(1, 1), new(0, 1), new(0.5f, 0), new(1, 0.5f), new(0.5f, 0.5f), new(0.5f, 1), new(0, 0.5f) }));
        }
    }
}
