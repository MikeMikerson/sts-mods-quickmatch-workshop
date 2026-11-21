using System.Collections.Generic;
using Godot;

namespace QuickMatch.Ui;

// The hub's card pictures, drawn on purpose as rough marker doodles: Play Now (a runner heading for the free
// spot in a waving group), Climb (a stick figure on a ladder up a spire) and Match Options (sliders and tick
// boxes). Rendered once at run time from the SVG text here, so the mod ships no image files and copies none
// of the game's. A build of Godot without SVG support gets null, and the card keeps the Host picture.
internal static class CardPictures
{
    // Rendered at 1.5 times the drawing's 400 by 300, a little larger than the card shows it.
    private const float Scale = 1.5f;

    private static readonly Dictionary<string, Texture2D?> Rendered = new();

    public static Texture2D? Play => Get(nameof(Play), PlaySvg);
    public static Texture2D? Climb => Get(nameof(Climb), ClimbSvg);
    public static Texture2D? Options => Get(nameof(Options), OptionsSvg);

    private static Texture2D? Get(string name, string svg)
    {
        if (Rendered.TryGetValue(name, out Texture2D? done))
        {
            return done;
        }
        Texture2D? texture = null;
        var image = new Image();
        Error error = image.LoadSvgFromString(svg, Scale);
        if (error == Error.Ok && !image.IsEmpty())
        {
            texture = ImageTexture.CreateFromImage(image);
        }
        else
        {
            MainFile.Logger.Warn($"card picture {name} not drawn ({error}); the card keeps the Host picture");
        }
        Rendered[name] = texture;
        return texture;
    }

    private const string PlaySvg = """
<svg xmlns='http://www.w3.org/2000/svg' width='400' height='300' viewBox='0 0 400 300' stroke-linecap='round' stroke-linejoin='round'>
<rect width='400' height='300' fill='#ffffff'/>
<path d='M20 250 Q73 252 123 246 Q171 247 230 253 Q298 254 377 247' fill='none' stroke='#262626' stroke-width='4.4'/>
<path d='M367 68 Q356 76 349 89 Q336 86 328 90 Q324 78 315 69 Q322 58 323 49 Q336 47 342 38 Q349 42 366 51 Q370 61 366 71 Z' fill='#ffd84a'/>
<path d='M365 54 Q355 68 350 73 Q341 79 327 89 Q321 77 308 69 Q313 59 319 43 Q332 36 342 35 Q355 38 360 45 Q360 61 365 72' fill='none' stroke='#e0a500' stroke-width='4.7'/>
<path d='M372 57 Q377 59 385 61' fill='none' stroke='#e0a500' stroke-width='3.9'/>
<path d='M363 89 Q370 91 373 96' fill='none' stroke='#e0a500' stroke-width='3.5'/>
<path d='M341 99 Q340 105 336 108' fill='none' stroke='#e0a500' stroke-width='3.6'/>
<path d='M316 89 Q308 90 303 95' fill='none' stroke='#e0a500' stroke-width='4.9'/>
<path d='M300 62 Q293 61 286 63' fill='none' stroke='#e0a500' stroke-width='3.5'/>
<path d='M314 31 Q309 29 300 25' fill='none' stroke='#e0a500' stroke-width='3.7'/>
<path d='M341 22 Q337 13 334 8' fill='none' stroke='#e0a500' stroke-width='3.5'/>
<path d='M362 35 Q368 31 370 24' fill='none' stroke='#e0a500' stroke-width='4.4'/>
<path d='M231 169 Q229 172 223 169 Q215 170 207 168 Q209 162 214 155 Q220 148 225 149 Q232 157 235 156 Q239 166 236 172 Q230 175 226 174' fill='none' stroke='#262626' stroke-width='5.3'/>
<path d='M222 179 Q216 197 219 214' fill='none' stroke='#262626' stroke-width='5.1'/>
<path d='M220 214 Q218 228 209 247' fill='none' stroke='#262626' stroke-width='4.0'/>
<path d='M220 213 Q230 230 233 243' fill='none' stroke='#262626' stroke-width='5.0'/>
<path d='M225 183 Q214 196 203 204' fill='none' stroke='#262626' stroke-width='6.1'/>
<path d='M225 183 Q236 183 241 177 Q244 169 243 154' fill='none' stroke='#262626' stroke-width='4.6'/>
<path d='M267 169 Q267 166 264 163 Q265 161 263 156 Q269 158 270 152 Q275 149 277 152 Q281 155 285 163 Q277 169 278 174 Q271 168 261 167' fill='none' stroke='#262626' stroke-width='4.6' stroke-dasharray='3 11'/>
<path d='M271 178 Q274 194 272 213' fill='none' stroke='#262626' stroke-width='5.8' stroke-dasharray='3 11'/>
<path d='M275 209 Q266 228 262 248' fill='none' stroke='#262626' stroke-width='5.4' stroke-dasharray='3 11'/>
<path d='M275 211 Q283 234 286 249' fill='none' stroke='#262626' stroke-width='5.0' stroke-dasharray='3 11'/>
<path d='M273 182 Q268 199 260 213' fill='none' stroke='#262626' stroke-width='4.5' stroke-dasharray='3 11'/>
<path d='M275 188 Q285 197 290 210' fill='none' stroke='#262626' stroke-width='6.0' stroke-dasharray='3 11'/>
<path d='M332 167 Q330 170 325 172 Q316 174 306 170 Q309 164 310 161 Q314 161 321 156 Q330 153 333 155 Q335 162 331 161 Q330 168 329 173' fill='none' stroke='#262626' stroke-width='4.5'/>
<path d='M322 176 Q327 192 323 208' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M324 213 Q313 230 309 248' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M326 215 Q326 230 333 246' fill='none' stroke='#262626' stroke-width='5.8'/>
<path d='M325 184 Q313 173 299 156' fill='none' stroke='#262626' stroke-width='4.3'/>
<path d='M324 180 Q335 166 342 156' fill='none' stroke='#262626' stroke-width='4.8'/>
<path d='M361 164 Q362 171 369 169 Q374 170 373 177 Q368 182 368 184 Q361 178 358 180 Q358 178 353 176 Q359 167 360 165 Q362 169 366 170' fill='none' stroke='#262626' stroke-width='4.0'/>
<path d='M360 188 Q359 204 365 221' fill='none' stroke='#262626' stroke-width='5.7'/>
<path d='M362 219 Q352 227 348 243' fill='none' stroke='#262626' stroke-width='5.2'/>
<path d='M362 218 Q364 234 370 243' fill='none' stroke='#262626' stroke-width='6.2'/>
<path d='M365 190 Q353 205 341 216' fill='none' stroke='#262626' stroke-width='4.7'/>
<path d='M362 193 Q372 188 377 189 Q377 181 379 169' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M104 165 Q103 155 109 148 Q116 146 123 152 Q125 159 129 163 Q127 167 122 175 Q121 177 113 175 Q107 173 104 167 Q104 162 103 156' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M114 177 Q113 191 103 214' fill='none' stroke='#262626' stroke-width='5.4'/>
<path d='M103 210 Q99 216 101 226 Q112 234 116 241' fill='none' stroke='#262626' stroke-width='5.1'/>
<path d='M105 213 Q95 225 84 233 Q72 233 68 225' fill='none' stroke='#262626' stroke-width='4.4'/>
<path d='M107 181 Q115 185 125 189 Q130 182 134 178' fill='none' stroke='#262626' stroke-width='4.6'/>
<path d='M109 184 Q97 189 90 202 Q80 198 76 190' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M24 176 Q33 187 54 186' fill='none' stroke='#8a8a8a' stroke-width='4.0'/>
<path d='M26 198 Q41 200 50 203' fill='none' stroke='#8a8a8a' stroke-width='5.0'/>
<path d='M23 217 Q33 213 51 222' fill='none' stroke='#8a8a8a' stroke-width='4.2'/>
<path d='M115 150 Q144 135 171 104 Q208 104 228 97 Q241 107 263 130' fill='none' stroke='#3d78d8' stroke-width='4.8' stroke-dasharray='2 12'/>
<path d='M250 125 Q259 130 265 132 Q272 118 271 113' fill='none' stroke='#3d78d8' stroke-width='4.6'/>
<path d='M12 12 Q198 1 395 0 Q396 145 392 288 Q200 291 3 297 Q5 146 7 2' fill='none' stroke='#262626' stroke-width='6'/>
</svg>
""";

    private const string ClimbSvg = """
<svg xmlns='http://www.w3.org/2000/svg' width='400' height='300' viewBox='0 0 400 300' stroke-linecap='round' stroke-linejoin='round'>
<rect width='400' height='300' fill='#ffffff'/>
<path d='M23 272 Q204 266 383 263' fill='none' stroke='#262626' stroke-width='4.2'/>
<path d='M155 271 L183 74 L233 71 L254 278 Z' fill='#9ab4e6'/>
<path d='M149 268 Q165 175 177 69 Q197 74 221 72 Q234 172 251 269' fill='none' stroke='#262626' stroke-width='5.5'/>
<path d='M167 69 Q185 49 200 38 Q212 52 233 72' fill='none' stroke='#262626' stroke-width='5.8'/>
<path d='M167 70 L198 33 L232 76 Z' fill='#e05a4a'/>
<path d='M203 35 Q198 27 199 12' fill='none' stroke='#262626' stroke-width='4.2'/>
<path d='M198 10 L230 23 L205 26 Z' fill='#e05a4a'/>
<path d='M200 17 Q208 19 227 24 Q206 32 197 32' fill='none' stroke='#262626' stroke-width='4.4'/>
<path d='M189 112 Q189 111 201 110 Q197 124 201 126 Q198 123 184 128 Q179 118 184 108' fill='none' stroke='#262626' stroke-width='2.6'/>
<path d='M212 153 Q220 150 222 149 Q227 161 227 164 Q221 162 211 167 Q210 159 215 153' fill='none' stroke='#262626' stroke-width='3.4'/>
<path d='M182 195 Q184 194 197 192 Q196 196 195 208 Q190 209 182 212 Q181 208 180 199' fill='none' stroke='#262626' stroke-width='2.4'/>
<path d='M209 234 Q217 234 219 235 Q222 242 220 247 Q214 247 207 246 Q206 242 209 229' fill='none' stroke='#262626' stroke-width='2.5'/>
<path d='M265 271 Q254 188 238 102' fill='none' stroke='#262626' stroke-width='3.7'/>
<path d='M281 267 Q266 184 256 104' fill='none' stroke='#262626' stroke-width='4.0'/>
<path d='M263 255 Q269 249 281 253' fill='none' stroke='#262626' stroke-width='2.7'/>
<path d='M256 239 Q266 231 277 233' fill='none' stroke='#262626' stroke-width='3.2'/>
<path d='M254 220 Q260 226 273 220' fill='none' stroke='#262626' stroke-width='3.3'/>
<path d='M248 200 Q255 203 270 197' fill='none' stroke='#262626' stroke-width='2.5'/>
<path d='M251 178 Q255 183 271 177' fill='none' stroke='#262626' stroke-width='3.3'/>
<path d='M244 164 Q251 162 270 157' fill='none' stroke='#262626' stroke-width='2.6'/>
<path d='M246 141 Q259 145 266 142' fill='none' stroke='#262626' stroke-width='3.2'/>
<path d='M242 121 Q255 116 258 119' fill='none' stroke='#262626' stroke-width='3.0'/>
<path d='M237 104 Q247 103 259 106' fill='none' stroke='#262626' stroke-width='3.3'/>
<path d='M262 126 Q256 123 255 114 Q256 110 255 99 Q261 100 267 98 Q272 104 279 108 Q283 117 280 118 Q275 120 266 123 Q259 122 261 116' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M267 119 Q270 139 264 155' fill='none' stroke='#262626' stroke-width='5.6'/>
<path d='M269 154 Q258 171 254 187' fill='none' stroke='#262626' stroke-width='5.2'/>
<path d='M268 152 Q269 170 276 186' fill='none' stroke='#262626' stroke-width='4.7'/>
<path d='M269 132 Q256 118 249 107' fill='none' stroke='#262626' stroke-width='6.2'/>
<path d='M266 128 Q280 116 288 107' fill='none' stroke='#262626' stroke-width='6.2'/>
<path d='M326 232 Q324 180 331 123' fill='none' stroke='#2f9e44' stroke-width='7.4'/>
<path d='M311 139 Q317 124 333 120 Q344 135 347 145' fill='none' stroke='#2f9e44' stroke-width='7.4'/>
<path d='M247 40 Q246 26 254 19 Q256 32 270 41 Q254 39 247 24 Q252 30 270 28 Q260 36 243 43' fill='none' stroke='#e0a500' stroke-width='4.2'/>
<path d='M132 41 Q123 38 117 32' fill='none' stroke='#e0a500' stroke-width='4.1'/>
<path d='M129 57 Q120 58 115 55' fill='none' stroke='#e0a500' stroke-width='4.3'/>
<path d='M11 7 Q203 11 394 6 Q394 147 393 287 Q200 299 5 300 Q12 155 11 1' fill='none' stroke='#262626' stroke-width='6'/>
</svg>
""";

    private const string OptionsSvg = """
<svg xmlns='http://www.w3.org/2000/svg' width='400' height='300' viewBox='0 0 400 300' stroke-linecap='round' stroke-linejoin='round'>
<rect width='400' height='300' fill='#ffffff'/>
<path d='M47 68 Q160 74 273 76' fill='none' stroke='#262626' stroke-width='5.8'/>
<path d='M111 72 Q113 67 111 54 Q115 56 127 56 Q128 62 138 71 Q134 80 135 82 Q131 87 123 85 Q114 85 110 80 Q109 73 104 65 Z' fill='#e05a4a'/>
<path d='M135 58 Q133 63 134 73 Q131 79 123 86 Q114 89 104 84 Q99 72 103 64 Q109 54 115 52 Q124 55 128 59 Q131 63 136 71' fill='none' stroke='#262626' stroke-width='6.2'/>
<path d='M53 135 Q161 127 273 118' fill='none' stroke='#262626' stroke-width='7.3'/>
<path d='M235 132 Q233 137 228 141 Q219 142 217 141 Q209 134 208 133 Q207 129 213 121 Q223 118 225 113 Q224 117 231 118 Q232 128 238 135 Z' fill='#3d78d8'/>
<path d='M211 105 Q224 111 231 112 Q234 122 230 126 Q230 132 228 138 Q223 137 218 134 Q214 131 202 133 Q204 122 205 112 Q212 107 223 108' fill='none' stroke='#262626' stroke-width='5.8'/>
<path d='M52 177 Q156 173 270 179' fill='none' stroke='#262626' stroke-width='6.6'/>
<path d='M175 169 Q176 179 177 181 Q175 188 170 195 Q164 194 159 201 Q154 195 150 190 Q150 182 148 177 Q148 176 154 170 Q161 166 172 167 Z' fill='#2f9e44'/>
<path d='M146 191 Q143 178 148 172 Q151 166 156 164 Q165 165 174 169 Q176 181 175 188 Q170 193 161 192 Q154 193 151 191 Q150 189 144 180' fill='none' stroke='#262626' stroke-width='4.9'/>
<path d='M63 221 Q85 224 99 219 Q100 234 96 259 Q79 253 60 259 Q64 235 67 219' fill='none' stroke='#262626' stroke-width='4.5'/>
<path d='M69 236 Q69 244 76 248 Q92 229 108 210' fill='none' stroke='#2f9e44' stroke-width='5.0'/>
<path d='M117 219 Q134 223 157 215 Q161 230 158 257 Q146 254 120 256 Q122 238 122 213' fill='none' stroke='#262626' stroke-width='3.6'/>
<path d='M124 237 Q132 245 137 254 Q143 237 161 214' fill='none' stroke='#2f9e44' stroke-width='4.2'/>
<path d='M183 220 Q199 218 219 220 Q221 236 218 254 Q195 255 179 259 Q175 235 182 220' fill='none' stroke='#262626' stroke-width='4.7'/>
<path d='M186 226 Q194 231 212 249' fill='none' stroke='#e05a4a' stroke-width='5.3'/>
<path d='M210 227 Q203 239 185 254' fill='none' stroke='#e05a4a' stroke-width='5.4'/>
<path d='M247 273 Q320 276 383 270' fill='none' stroke='#262626' stroke-width='3.9'/>
<path d='M323 192 Q322 188 321 179 Q320 176 322 173 Q330 171 341 168 Q340 173 341 182 Q338 193 334 196 Q334 199 325 193 Q318 192 314 183' fill='none' stroke='#262626' stroke-width='4.3'/>
<path d='M328 194 Q328 210 331 232' fill='none' stroke='#262626' stroke-width='5.6'/>
<path d='M331 234 Q328 251 317 273' fill='none' stroke='#262626' stroke-width='5.9'/>
<path d='M331 236 Q335 251 341 274' fill='none' stroke='#262626' stroke-width='5.2'/>
<path d='M329 207 Q313 205 291 199' fill='none' stroke='#262626' stroke-width='4.8'/>
<path d='M330 205 Q339 216 346 231' fill='none' stroke='#262626' stroke-width='5.1'/>
<path d='M7 11 Q202 12 396 5 Q395 141 395 288 Q202 300 8 300 Q9 155 13 5' fill='none' stroke='#262626' stroke-width='6'/>
</svg>
""";
}
