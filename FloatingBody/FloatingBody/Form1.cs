using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace FloatingBodyStability
{
    // Главный класс формы – наследуется от Form (стандартное окно Windows Forms)
    public partial class Form1 : Form
    {
        // ПОЛЯ (ДАННЫЕ) 
        // Геометрия и физические параметры
        private List<PointF> bodyVertices;   // Список вершин тела в локальной системе координат (центр масс в 0,0)
        private float mass;                  // Масса тела, кг
        private float densityFluid = 1000f;  // Плотность жидкости, кг/м^3 (по умолчанию вода)
        private float gravity = 9.81f;       // Ускорение свободного падения, м/с^2

        // Текущее состояние (положение тела и воды)
        private float waterY = 0f;           // Уровень воды в мировых координатах (м)
        private float tiltAngle = 0f;        // Угол крена тела в радианах

        // Результаты расчёта подводной части
        private float submergedArea = 0f;     // Площадь подводной части (м^2) – в 2D модели это объём на единицу глубины
        private PointF submergedCentroid;     // Центр водоизмещения (ЦВ) – центр тяжести подводной части

        // Элементы пользовательского интерфейса (ссылки на созданные объекты)
        private PictureBox picCanvas;         // Область для рисования
        private TrackBar trackAngle;          // Ползунок угла наклона
        private NumericUpDown numWaterLevel;  // Поле для ввода уровня воды
        private NumericUpDown numMass;        // Поле для ввода массы
        private NumericUpDown numDensity;     // Поле для ввода плотности жидкости
        private ComboBox cmbShape;            // Выпадающий список для выбора формы тела
        private Button btnFindEquilibrium;    // Кнопка "Найти равновесие"
        private Button btnReset;              // Кнопка "Сброс"
        private Button btnApply;              // Кнопка "Применить параметры"
        private Label lblMetaHeight;          // Метка для вывода метацентрической высоты
        private Label lblStatus;              // Метка для вывода статуса/ошибок

        // Константы для численных методов
        private const float SEARCH_TOLERANCE = 0.0001f;   // Точность поиска корня (невязка)
        private const int MAX_ITER = 100;                 // Максимальное число итераций

        //  КОНСТРУКТОР 
        public Form1()
        {
            // 1. Инициализация геометрии и массы по умолчанию (прямоугольник 2x1 м, масса 1000 кг)
            InitializeDefaultGeometry();
            // 2. Создание всех элементов управления (кнопки, ползунки, поля ввода)
            SetupUI();
            // 3. Первый пересчёт подводной части и перерисовка
            UpdateVisualization();
        }

        // НАЧАЛЬНАЯ ГЕОМЕТРИЯ ПО УМОЛЧАНИЮ 
        private void InitializeDefaultGeometry()
        {
            // Прямоугольник шириной 2 м (от -1 до 1) и высотой 1 м (от -0.5 до 0.5)
            bodyVertices = new List<PointF>
            {
                new PointF(-1.0f, -0.5f),   // левая нижняя вершина
                new PointF( 1.0f, -0.5f),   // правая нижняя
                new PointF( 1.0f,  0.5f),   // правая верхняя
                new PointF(-1.0f,  0.5f)    // левая верхняя
            };
            mass = 1000f;                   // масса 1000 кг – средняя плотность 500 кг/м³, тело плавает наполовину
        }

        // СОЗДАНИЕ ИНТЕРФЕЙСА 
        private void SetupUI()
        {
            this.Text = "Анализ устойчивости плавающего тела";
            this.Size = new Size(900, 700);
            this.StartPosition = FormStartPosition.CenterScreen;

            // Область рисования (PictureBox)
            picCanvas = new PictureBox
            {
                Location = new Point(20, 20),
                Size = new Size(600, 500),
                BackColor = Color.LightSteelBlue,
                BorderStyle = BorderStyle.FixedSingle
            };
            picCanvas.Paint += PicCanvas_Paint;   // подписка на событие перерисовки

            // Ползунок угла наклона (от -45° до +45°)
            Label lblAngle = new Label { Text = "Угол наклона (градусы):", Location = new Point(640, 30), AutoSize = true };
            trackAngle = new TrackBar
            {
                Location = new Point(640, 50),
                Size = new Size(200, 45),
                Minimum = -45,
                Maximum = 45,
                Value = 0,
                TickFrequency = 10
            };
            // При изменении положения ползунка пересчитываем угол в радианах и обновляем картинку
            trackAngle.ValueChanged += (s, e) => { tiltAngle = (float)(trackAngle.Value * Math.PI / 180.0); UpdateVisualization(); };

            // Поле для ввода уровня воды
            Label lblWater = new Label { Text = "Уровень воды (Y, м):", Location = new Point(640, 100), AutoSize = true };
            numWaterLevel = new NumericUpDown
            {
                Location = new Point(640, 120),
                Size = new Size(100, 25),
                DecimalPlaces = 2,
                Minimum = -2m,
                Maximum = 2m,
                Increment = 0.05m,
                Value = 0m
            };
            numWaterLevel.ValueChanged += (s, e) => { waterY = (float)numWaterLevel.Value; UpdateVisualization(); };

            // Кнопка "Найти равновесие"
            btnFindEquilibrium = new Button
            {
                Text = "Найти равновесие",
                Location = new Point(640, 160),
                Size = new Size(150, 40),
                BackColor = Color.LightGreen
            };
            btnFindEquilibrium.Click += BtnFindEquilibrium_Click;

            // Кнопка "Сброс" – возвращает угол и уровень воды в нулевые значения
            btnReset = new Button
            {
                Text = "Сброс",
                Location = new Point(800, 160),
                Size = new Size(80, 40)
            };
            btnReset.Click += (s, e) => { trackAngle.Value = 0; numWaterLevel.Value = 0; UpdateVisualization(); };

            // Метка для вывода метацентрической высоты
            lblMetaHeight = new Label
            {
                Location = new Point(640, 220),
                AutoSize = true,
                Font = new Font("Arial", 10, FontStyle.Bold),
                ForeColor = Color.DarkBlue
            };

            // Метка для статуса или сообщений об ошибках
            lblStatus = new Label
            {
                Location = new Point(640, 260),
                AutoSize = true,
                Font = new Font("Arial", 9, FontStyle.Italic),
                ForeColor = Color.DarkRed
            };

            // ДОБАВЛЕННЫЕ ЭЛЕМЕНТЫ ДЛЯ ИЗМЕНЕНИЯ ПАРАМЕТРОВ 
            // Поле для массы
            Label lblMass = new Label { Text = "Масса (кг):", Location = new Point(640, 300), AutoSize = true };
            numMass = new NumericUpDown
            {
                Location = new Point(640, 320),
                Size = new Size(100, 25),
                DecimalPlaces = 0,
                Minimum = 100,
                Maximum = 5000,
                Increment = 50,
                Value = 1000
            };

            // Поле для плотности жидкости
            Label lblDensity = new Label { Text = "Плотность (кг/м³):", Location = new Point(640, 350), AutoSize = true };
            numDensity = new NumericUpDown
            {
                Location = new Point(640, 370),
                Size = new Size(100, 25),
                DecimalPlaces = 0,
                Minimum = 500,
                Maximum = 1500,
                Increment = 50,
                Value = 1000
            };

            // Выпадающий список выбора формы тела
            Label lblShape = new Label { Text = "Форма тела:", Location = new Point(640, 400), AutoSize = true };
            cmbShape = new ComboBox
            {
                Location = new Point(640, 420),
                Size = new Size(160, 25),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            cmbShape.Items.AddRange(new object[] { "Прямоугольник", "Круг", "Эллипс", "Полукруг (плоской стороной вверх)" });
            cmbShape.SelectedIndex = 0;

            // Кнопка "Применить параметры" – загружает выбранную форму и новые массу/плотность
            btnApply = new Button
            {
                Text = "Применить параметры",
                Location = new Point(640, 460),
                Size = new Size(150, 40),
                BackColor = Color.LightYellow
            };
            btnApply.Click += BtnApply_Click;

            // Добавляем все элементы управления на форму
            this.Controls.Add(picCanvas);
            this.Controls.Add(lblAngle);
            this.Controls.Add(trackAngle);
            this.Controls.Add(lblWater);
            this.Controls.Add(numWaterLevel);
            this.Controls.Add(btnFindEquilibrium);
            this.Controls.Add(btnReset);
            this.Controls.Add(lblMetaHeight);
            this.Controls.Add(lblStatus);
            this.Controls.Add(lblMass);
            this.Controls.Add(numMass);
            this.Controls.Add(lblDensity);
            this.Controls.Add(numDensity);
            this.Controls.Add(lblShape);
            this.Controls.Add(cmbShape);
            this.Controls.Add(btnApply);
        }

        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ ДЛЯ СОЗДАНИЯ ГЕОМЕТРИИ
        // Создаёт круг (аппроксимированный многоугольником) радиуса radius из segments вершин
        private List<PointF> CreateCircle(float radius, int segments)
        {
            var verts = new List<PointF>();
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * (float)Math.PI * i / segments;   // угол от 0 до 2π
                float x = radius * (float)Math.Cos(angle);
                float y = radius * (float)Math.Sin(angle);
                verts.Add(new PointF(x, y));
            }
            return verts;
        }

        // Создаёт эллипс с полуосями a (по X) и b (по Y) из segments вершин
        private List<PointF> CreateEllipse(float a, float b, int segments)
        {
            var verts = new List<PointF>();
            for (int i = 0; i < segments; i++)
            {
                float angle = 2 * (float)Math.PI * i / segments;
                float x = a * (float)Math.Cos(angle);
                float y = b * (float)Math.Sin(angle);
                verts.Add(new PointF(x, y));
            }
            return verts;
        }

        // Создаёт полукруг плоской стороной вверх (дуга вниз) радиуса radius, segments точек на дуге
        private List<PointF> CreateSemicircle(float radius, int segments)
        {
            var verts = new List<PointF>();
            // Левая крайняя точка плоской стороны
            verts.Add(new PointF(-radius, 0));
            // Точки дуги (от левого до правого края) – y отрицательный (вниз)
            for (int i = 1; i < segments; i++)
            {
                float angle = (float)Math.PI * ((float)i / segments);   // угол от 0 до π
                float x = radius * (float)Math.Cos(angle);
                float y = -radius * (float)Math.Sin(angle);   // минус – дуга вниз
                verts.Add(new PointF(x, y));
            }
            // Правая крайняя точка
            verts.Add(new PointF(radius, 0));
            return verts;
        }

        //  ОТРИСОВКА (ВИЗУАЛИЗАЦИЯ) 
        // Этот метод вызывается автоматически при каждом Invalidate() на picCanvas
        private void PicCanvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;   // сглаживание

            // Масштаб: 1 метр = 120 пикселей. Смещение: центр экрана в (300,250)
            float scale = 120f;
            float offsetX = 300f;
            float offsetY = 250f;

            // Экранная координата уровня воды (Y экрана растёт вниз, а физическая Y – вверх)
            float waterScreenY = offsetY - waterY * scale;

            // Заливаем воду: полупрозрачный синий прямоугольник от уровня воды до низа
            using (Brush waterBrush = new SolidBrush(Color.FromArgb(100, 0, 150, 255)))
            {
                RectangleF waterRect = new RectangleF(0, waterScreenY, picCanvas.Width, picCanvas.Height - waterScreenY);
                g.FillRectangle(waterBrush, waterRect);
            }
            // Рисуем линию уровня воды
            using (Pen waterPen = new Pen(Color.Blue, 2))
            {
                g.DrawLine(waterPen, 0, waterScreenY, picCanvas.Width, waterScreenY);
            }

            // Поворачиваем вершины тела на угол tiltAngle (матрица поворота)
            List<PointF> worldVertices = new List<PointF>();
            foreach (var v in bodyVertices)
            {
                float xr = v.X * (float)Math.Cos(tiltAngle) - v.Y * (float)Math.Sin(tiltAngle);
                float yr = v.X * (float)Math.Sin(tiltAngle) + v.Y * (float)Math.Cos(tiltAngle);
                worldVertices.Add(new PointF(xr, yr));
            }

            // Переводим мировые координаты в экранные
            PointF[] screenPoints = worldVertices.Select(p => new PointF(offsetX + p.X * scale, offsetY - p.Y * scale)).ToArray();

            // Рисуем тело: заливаем серым с прозрачностью, обводим чёрным контуром
            using (Brush bodyBrush = new SolidBrush(Color.FromArgb(180, 200, 200, 200)))
            using (Pen bodyPen = new Pen(Color.Black, 2))
            {
                if (screenPoints.Length >= 3)
                {
                    g.FillPolygon(bodyBrush, screenPoints);
                    g.DrawPolygon(bodyPen, screenPoints);
                }
            }

            // Центр масс (ЦМ) – всегда в (0,0). Рисуем красную точку и подпись
            PointF cmScreen = new PointF(offsetX, offsetY);
            using (Brush brush = new SolidBrush(Color.Red))
            {
                g.FillEllipse(brush, cmScreen.X - 4, cmScreen.Y - 4, 8, 8);
                g.DrawString("ЦМ", new Font("Arial", 8), Brushes.Red, cmScreen.X + 5, cmScreen.Y - 5);
            }

            // Центр водоизмещения (ЦВ) – рисуем зелёным, если есть погружённая часть
            if (submergedArea > 0)
            {
                PointF cbWorld = submergedCentroid;
                PointF cbScreen = new PointF(offsetX + cbWorld.X * scale, offsetY - cbWorld.Y * scale);
                using (Brush brush = new SolidBrush(Color.Green))
                {
                    g.FillEllipse(brush, cbScreen.X - 4, cbScreen.Y - 4, 8, 8);
                    g.DrawString("ЦВ", new Font("Arial", 8), Brushes.Green, cbScreen.X + 5, cbScreen.Y - 5);
                }
            }

            // Выводим текущие значения угла и уровня воды
            g.DrawString($"Угол: {tiltAngle * 180 / Math.PI:F1}°", this.Font, Brushes.Black, 10, 10);
            g.DrawString($"Уровень воды: {waterY:F2} м", this.Font, Brushes.Black, 10, 30);
        }

        // ГЕОМЕТРИЧЕСКИЕ АЛГОРИТМЫ 
        // Алгоритм Сазерленда–Ходжмана: отсечение многоугольника полуплоскостью y <= waterY.
        // Возвращает многоугольник, лежащий ниже уровня воды (подводная часть).
        private List<PointF> ClipPolygonByWater(List<PointF> worldVertices, float waterY)
        {
            List<PointF> output = new List<PointF>();
            if (worldVertices.Count == 0) return output;

            // Вспомогательная функция: пересечение отрезка (p1-p2) с горизонтальной линией y
            PointF Intersect(PointF p1, PointF p2, float y)
            {
                float t = (y - p1.Y) / (p2.Y - p1.Y);   // параметр линейной интерполяции
                float x = p1.X + t * (p2.X - p1.X);
                return new PointF(x, y);
            }

            List<PointF> input = new List<PointF>(worldVertices);
            output.Clear();

            // Проход по всем рёбрам
            for (int i = 0; i < input.Count; i++)
            {
                PointF curr = input[i];
                PointF next = input[(i + 1) % input.Count];
                bool currInside = curr.Y <= waterY;   // текущая вершина ниже/на уровне воды?
                bool nextInside = next.Y <= waterY;   // следующая вершина?

                if (currInside && nextInside)
                {
                    // Обе внутри – добавляем следующую вершину
                    output.Add(next);
                }
                else if (currInside && !nextInside)
                {
                    // Изнутри наружу – добавляем точку пересечения
                    output.Add(Intersect(curr, next, waterY));
                }
                else if (!currInside && nextInside)
                {
                    // Снаружи внутрь – добавляем пересечение и следующую вершину
                    output.Add(Intersect(curr, next, waterY));
                    output.Add(next);
                }
                // else оба снаружи – ничего не добавляем
            }
            return output;
        }

        // Вычисление площади многоугольника по формуле Гаусса (метод трапеций)
        private float PolygonArea(List<PointF> poly)
        {
            if (poly.Count < 3) return 0;
            float area = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                PointF p1 = poly[i];
                PointF p2 = poly[(i + 1) % poly.Count];
                area += p1.X * p2.Y - p2.X * p1.Y;
            }
            return Math.Abs(area) * 0.5f;
        }

        // Вычисление центроида (центра тяжести) однородного многоугольника
        private PointF PolygonCentroid(List<PointF> poly)
        {
            if (poly.Count < 3) return new PointF(0, 0);
            float area = PolygonArea(poly);
            if (area == 0) return new PointF(0, 0);
            float cx = 0, cy = 0;
            for (int i = 0; i < poly.Count; i++)
            {
                PointF p1 = poly[i];
                PointF p2 = poly[(i + 1) % poly.Count];
                float cross = p1.X * p2.Y - p2.X * p1.Y;
                cx += (p1.X + p2.X) * cross;
                cy += (p1.Y + p2.Y) * cross;
            }
            cx /= (6 * area);
            cy /= (6 * area);
            return new PointF(cx, cy);
        }

        // Обновляет значения submergedArea и submergedCentroid для текущих waterY и tiltAngle
        private void UpdateSubmergedProperties()
        {
            // Поворачиваем вершины тела на текущий угол
            List<PointF> worldVertices = new List<PointF>();
            foreach (var v in bodyVertices)
            {
                float xr = v.X * (float)Math.Cos(tiltAngle) - v.Y * (float)Math.Sin(tiltAngle);
                float yr = v.X * (float)Math.Sin(tiltAngle) + v.Y * (float)Math.Cos(tiltAngle);
                worldVertices.Add(new PointF(xr, yr));
            }

            // Отсекаем часть выше воды
            List<PointF> submergedPoly = ClipPolygonByWater(worldVertices, waterY);
            submergedArea = PolygonArea(submergedPoly);
            submergedCentroid = PolygonCentroid(submergedPoly);
        }

        // РАСЧЁТ МЕТАЦЕНТРИЧЕСКОЙ ВЫСОТЫ 
        private void ComputeMetaCentricHeight()
        {
            if (submergedArea <= 0)
            {
                lblMetaHeight.Text = "Метацентрическая высота: тело не плавает (над водой)";
                lblMetaHeight.ForeColor = Color.DarkBlue;
                return;
            }

            // Поворачиваем вершины для поиска ватерлинии
            List<PointF> worldVertices = new List<PointF>();
            foreach (var v in bodyVertices)
            {
                float xr = v.X * (float)Math.Cos(tiltAngle) - v.Y * (float)Math.Sin(tiltAngle);
                float yr = v.X * (float)Math.Sin(tiltAngle) + v.Y * (float)Math.Cos(tiltAngle);
                worldVertices.Add(new PointF(xr, yr));
            }

            // Находим пересечения рёбер с уровнем воды – это даст X-координаты ватерлинии
            List<float> intersectX = new List<float>();
            for (int i = 0; i < worldVertices.Count; i++)
            {
                PointF p1 = worldVertices[i];
                PointF p2 = worldVertices[(i + 1) % worldVertices.Count];
                if ((p1.Y - waterY) * (p2.Y - waterY) < 0)   // ребро пересекает уровень воды
                {
                    float t = (waterY - p1.Y) / (p2.Y - p1.Y);
                    float x = p1.X + t * (p2.X - p1.X);
                    intersectX.Add(x);
                }
            }

            if (intersectX.Count >= 2)
            {
                float width = Math.Abs(intersectX.Max() - intersectX.Min());   // ширина ватерлинии L
                // Момент инерции отрезка (в 2D) = L^3 / 12
                float I = width * width * width / 12f;   // width^3 / 12
                float r = I / submergedArea;             // метацентрический радиус
                float a = Math.Abs(0 - submergedCentroid.Y); // расстояние по вертикали между ЦМ (y=0) и ЦВ
                float metaHeight = r - a;                // метацентрическая высота h
                lblMetaHeight.Text = $"Метацентрическая высота: {metaHeight:F3} м";
                lblMetaHeight.ForeColor = metaHeight > 0 ? Color.Green : Color.Red;
            }
            else
            {
                lblMetaHeight.Text = "Метацентрическая высота: не определена (тело полностью погружено?)";
                lblMetaHeight.ForeColor = Color.DarkOrange;
            }
        }

        // Обновляет визуализацию и все расчёты (вызывается после изменения любых параметров)
        private void UpdateVisualization()
        {
            UpdateSubmergedProperties();   // пересчитать подводную часть
            ComputeMetaCentricHeight();    // вычислить метацентрическую высоту
            picCanvas.Invalidate();        // вызвать перерисовку
        }

        //  ФУНКЦИИ НЕВЯЗКИ ДЛЯ ЧИСЛЕННОГО ПОИСКА
        // Возвращает разницу между силой Архимеда и весом для заданного уровня воды y и угла angle.
        // Временно подменяет глобальные waterY, tiltAngle, затем восстанавливает.
        private float ForceError(float y, float angle)
        {
            float oldY = waterY;
            float oldAng = tiltAngle;
            waterY = y;
            tiltAngle = angle;
            UpdateSubmergedProperties();
            float buoyancy = densityFluid * gravity * submergedArea;
            float weight = mass * gravity;
            float err = buoyancy - weight;   // должно быть 0 в равновесии
            waterY = oldY;
            tiltAngle = oldAng;
            return err;
        }

        // Возвращает горизонтальное смещение центра водоизмещения относительно центра масс (плечо момента).
        // Для равновесия требуется x_c = 0.
        private float MomentError(float angle, float y)
        {
            float oldY = waterY;
            float oldAng = tiltAngle;
            waterY = y;
            tiltAngle = angle;
            UpdateSubmergedProperties();
            float err = submergedCentroid.X;   // центр масс в (0,0)
            waterY = oldY;
            tiltAngle = oldAng;
            return err;
        }

        // МЕТОД ПОЛОВИННОГО ДЕЛЕНИЯ (БИСЕКЦИЯ) С РАСШИРЕНИЕМ ИНТЕРВАЛА
        // Ищет корень уравнения func(x)=0 на интервале [left, right] с заданной точностью.
        // Если знаки функции на концах одинаковые, пытается расширить интервал (в 2,4,8 раз).
        private float FindRootBisection(Func<float, float> func, float left, float right, float tolerance, int maxIter)
        {
            float fLeft = func(left);
            float fRight = func(right);

            if (Math.Abs(fLeft) < tolerance) return left;
            if (Math.Abs(fRight) < tolerance) return right;

            // Если знаки одинаковые, пытаемся расширить интервал
            if (fLeft * fRight >= 0)
            {
                for (float expand = 2.0f; expand <= 8.0f; expand *= 2.0f)
                {
                    float newLeft = left - (right - left) * (expand - 1);
                    float newRight = right + (right - left) * (expand - 1);
                    float newFLeft = func(newLeft);
                    float newFRight = func(newRight);
                    if (newFLeft * newFRight < 0)
                    {
                        left = newLeft;
                        right = newRight;
                        fLeft = newFLeft;
                        fRight = newFRight;
                        break;
                    }
                    if (expand >= 8.0f) return float.NaN;
                }
                if (fLeft * fRight >= 0) return float.NaN;
            }

            // Классический алгоритм бисекции
            for (int i = 0; i < maxIter; i++)
            {
                float mid = (left + right) * 0.5f;
                float fMid = func(mid);
                if (Math.Abs(fMid) < tolerance) return mid;
                if (fLeft * fMid < 0)
                {
                    right = mid;
                    fRight = fMid;
                }
                else
                {
                    left = mid;
                    fLeft = fMid;
                }
            }
            return (left + right) * 0.5f;
        }

        //ОБРАБОТЧИК КНОПКИ "НАЙТИ РАВНОВЕСИЕ"
        private void BtnFindEquilibrium_Click(object sender, EventArgs e)
        {
            float oldWaterY = waterY;
            float oldAngle = tiltAngle;

            try
            {
                // Определяем интервал поиска по геометрии тела
                float minYbody = bodyVertices.Min(v => v.Y);
                float maxYbody = bodyVertices.Max(v => v.Y);
                float maxOffset = (float)Math.Sqrt(bodyVertices.Max(v => v.X * v.X + v.Y * v.Y));
                float searchMin = minYbody - maxOffset;
                float searchMax = maxYbody + maxOffset;

                float newWaterY = waterY;
                float newAngle = tiltAngle;

                // ========== ИТЕРАЦИОННЫЙ ЦИКЛ ==========
                for (int iter = 0; iter < 10; iter++)   // 10 итераций для стабилизации
                {
                    // Уточняем глубину при текущем угле
                    newWaterY = FindRootBisection(y => ForceError(y, newAngle),
                                   searchMin, searchMax, SEARCH_TOLERANCE, MAX_ITER);
                    if (float.IsNaN(newWaterY))
                        throw new Exception("Не удалось найти уровень воды");

                    // Уточняем угол при найденной глубине
                    newAngle = FindRootBisection(ang => MomentError(ang, newWaterY),
                                 -0.785f, 0.785f, SEARCH_TOLERANCE, MAX_ITER);
                    if (float.IsNaN(newAngle))
                        throw new Exception("Не удалось найти равновесный угол");
                }

                waterY = newWaterY;
                tiltAngle = newAngle;
                numWaterLevel.Value = (decimal)waterY;

                int angleDeg = (int)(tiltAngle * 180 / Math.PI);
                if (angleDeg < trackAngle.Minimum) angleDeg = trackAngle.Minimum;
                if (angleDeg > trackAngle.Maximum) angleDeg = trackAngle.Maximum;
                trackAngle.Value = angleDeg;

                UpdateVisualization();
                lblStatus.Text = "Равновесие найдено (силы и моменты).";
                lblStatus.ForeColor = Color.DarkGreen;
            }
            catch (Exception ex)
            {
                waterY = oldWaterY;
                tiltAngle = oldAngle;
                numWaterLevel.Value = (decimal)waterY;
                trackAngle.Value = (int)(tiltAngle * 180 / Math.PI);
                UpdateVisualization();
                lblStatus.Text = $"Ошибка: {ex.Message}";
                lblStatus.ForeColor = Color.Red;
            }
        }

        // ОБРАБОТЧИК КНОПКИ "ПРИМЕНИТЬ ПАРАМЕТРЫ"
        private void BtnApply_Click(object sender, EventArgs e)
        {
            // Считываем новые значения массы и плотности
            mass = (float)numMass.Value;
            densityFluid = (float)numDensity.Value;

            // В зависимости от выбранной формы создаём новый список вершин
            switch (cmbShape.SelectedIndex)
            {
                case 0: // Прямоугольник
                    bodyVertices = new List<PointF>
                    {
                        new PointF(-1.0f, -0.5f),
                        new PointF( 1.0f, -0.5f),
                        new PointF( 1.0f,  0.5f),
                        new PointF(-1.0f,  0.5f)
                    };
                    break;
                case 1: // Круг (радиус 1 м, 32 точки)
                    bodyVertices = CreateCircle(1.0f, 32);
                    break;
                case 2: // Эллипс (полуоси 1.0 и 0.5)
                    bodyVertices = CreateEllipse(1.0f, 0.5f, 32);
                    break;
                case 3: // Полукруг (плоской стороной вверх, радиус 1 м)
                    bodyVertices = CreateSemicircle(1.0f, 24);
                    break;
                default:
                    bodyVertices = new List<PointF>
                    {
                        new PointF(-1.0f, -0.5f),
                        new PointF( 1.0f, -0.5f),
                        new PointF( 1.0f,  0.5f),
                        new PointF(-1.0f,  0.5f)
                    };
                    break;
            }

            // Сбрасываем угол и уровень воды в нулевые значения, чтобы начать с чистого листа
            tiltAngle = 0;
            waterY = 0;
            trackAngle.Value = 0;
            numWaterLevel.Value = 0;

            UpdateVisualization();
            lblStatus.Text = "Параметры изменены. Нажмите 'Найти равновесие'.";
        }
    }
}