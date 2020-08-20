﻿using Harmony;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;

namespace InputFix
{
    public delegate void KeyEventHandler(object sender, KeyEventArgs e);

    public delegate void CharEnteredHandler(object sender, CharacterEventArgs e);

    public static class KeyboardInput_
    {
        public static event CharEnteredHandler CharEntered;

        public static event KeyEventHandler KeyDown;

        public static event KeyEventHandler KeyUp;

        #region Dll Import

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll", CharSet = CharSet.Auto, ExactSpelling = true)]
        internal static extern int MapWindowPoints(IntPtr hWndFrom, IntPtr hWndTo, ref RECT pt, int cPoints);

        #endregion Dll Import

        #region WM_MSG

        private const int WM_KEYDOWN = 0x100;
        private const int WM_KEYUP = 0x101;
        private const int WM_CHAR = 0x102;
        private const int WM_MOUSEMOVE = 0x0200;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_LBUTTONUP = 0x0202;

        private const int DLGC_WANTALLKEYS = 4;
        private const int WM_GETDLGCODE = 135;
        private const int GWL_WNDPROC = -4;

        #endregion WM_MSG

        public static void Initialize(GameWindow window)
        {
            if (initialized)
            {
                throw new InvalidOperationException("KeyboardInput.Initialize can only be called once!");
            }
            hookProcDelegate = new WndProc(HookProc);
            //Init IME
            ImeSharp.InputMethod.Initialize(window.Handle);
            ImeSharp.InputMethod.TextComposition += InputMethod_TextComposition;
            ImeSharp.InputMethod.TextInput += InputMethod_TextInput;

            SetWindowLong(window.Handle, GWL_WNDPROC, (int)Marshal.GetFunctionPointerForDelegate(hookProcDelegate));

            prevWndProc = (IntPtr)Traverse.Create(typeof(KeyboardInput)).Field("prevWndProc").GetValue();

            CharEntered += KeyboardInput__CharEntered;
            KeyDown += KeyboardInput__KeyDown;

            ModEntry._helper.Events.Display.RenderedActiveMenu += Display_RenderedActiveMenu; ;
            initialized = true;
        }

        #region KeyboardDispatcher

        private static void KeyboardInput__KeyDown(object sender, KeyEventArgs e)
        {
            Game1.keyboardDispatcher.Subscriber?.RecieveSpecialInput(e.KeyCode);
        }

        private static void KeyboardInput__CharEntered(object sender, CharacterEventArgs e)
        {
            if (!char.IsControl(e.Character))
            {
                Game1.keyboardDispatcher.Subscriber?.RecieveTextInput(e.Character);
                return;
            }
            if (e.Character == '\u0016')
            {
                if (System.Windows.Forms.Clipboard.ContainsText())
                    Game1.keyboardDispatcher.Subscriber?.RecieveTextInput(System.Windows.Forms.Clipboard.GetText());
                return;
            }
            Game1.keyboardDispatcher.Subscriber?.RecieveCommandInput(e.Character);
        }

        #endregion KeyboardDispatcher

        #region HandleImeSharpEvent

        private static Composition comp = new Composition();

        private static void Display_RenderedActiveMenu(object sender, StardewModdingAPI.Events.RenderedActiveMenuEventArgs e)
        {
            comp.Draw(e.SpriteBatch);
        }

        private static void InputMethod_TextInput(object sender, ImeSharp.TextInputEventArgs e)
        {
            CharEntered?.Invoke(null, new CharacterEventArgs(e.Character, 0));
        }

        private static void InputMethod_TextComposition(object sender, ImeSharp.TextCompositionEventArgs e)
        {
            comp.text = e.CompositionText.ToString();
            comp.caret = e.CursorPosition;

            ITextBox textBox_ = Game1.keyboardDispatcher.Subscriber as ITextBox;
            TextBox textBox = Game1.keyboardDispatcher.Subscriber as TextBox;

            Vector2 vector2 = textBox.Font.MeasureString(comp.text);
            if (textBox_ != null)
            {
                Acp acp = textBox_.GetSelection();
                RECT rECT = textBox_.GetTextExt(new Acp(0, acp.Start));
                ImeSharp.InputMethod.SetTextInputRect(rECT.right, rECT.top, (int)vector2.X, 32);
            }
            else if (textBox != null)//without ITextBox interface
            {
                ImeSharp.InputMethod.SetTextInputRect(
                    //if without textbox, we can only insert at end
                    textBox.X + (int)textBox.Font.MeasureString(textBox.Text).X + (textBox is TextBox ? 16 : 12),
                    textBox.Y,
                    (int)vector2.X,
                    32);
            }
        }

        #endregion HandleImeSharpEvent

        private static RECT MouseSelection = new RECT();
        private static bool Selecting = false;

        private static IntPtr HookProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            IntPtr returnCode = CallWindowProc(prevWndProc, hWnd, msg, wParam, lParam);
            switch (msg)
            {
                case WM_GETDLGCODE:
                    returnCode = (IntPtr)DLGC_WANTALLKEYS;
                    break;

                case WM_CHAR:
                    CharEntered?.Invoke(null, new CharacterEventArgs((char)wParam, (int)lParam));
                    break;

                case WM_KEYDOWN:
                    KeyDown?.Invoke(null, new KeyEventArgs((Keys)wParam));
                    break;

                case WM_KEYUP:
                    KeyUp?.Invoke(null, new KeyEventArgs((Keys)wParam));
                    break;

                case WM_LBUTTONDOWN:
                    MouseSelection.left = (int)lParam & 0xffff;
                    MouseSelection.top = (int)lParam >> 16;
                    MouseSelection.right = MouseSelection.left;
                    MouseSelection.bottom = MouseSelection.top;
                    if (Game1.keyboardDispatcher.Subscriber is ITextBox)
                    {
                        ITextBox textBox = Game1.keyboardDispatcher.Subscriber as ITextBox;
                        Acp acp = textBox.GetAcpByRange(MouseSelection);
                        if (acp.Start >= 0)
                        {
                            textBox.SetSelection(acp.Start, acp.End);
                            Selecting = true;
                        }
                    }
                    break;

                case WM_MOUSEMOVE:
                    MouseSelection.right = (int)lParam & 0xffff;
                    MouseSelection.bottom = (int)lParam >> 16;
                    if (Selecting && Game1.keyboardDispatcher.Subscriber is ITextBox)
                    {
                        RECT range = new RECT
                        {
                            left = Math.Min(MouseSelection.left, MouseSelection.right),
                            top = Math.Max(MouseSelection.top, MouseSelection.bottom),
                            right = Math.Max(MouseSelection.left, MouseSelection.right),
                            bottom = Math.Min(MouseSelection.top, MouseSelection.bottom)
                        };
                        ITextBox textBox = Game1.keyboardDispatcher.Subscriber as ITextBox;
                        Acp acp = textBox.GetAcpByRange(range);
                        if (acp.Start >= 0)
                        {
                            textBox.SetSelection(acp.Start, acp.End);
                            textBox.SetSelState(MouseSelection.left > MouseSelection.right ? SelState.SEL_AE_END : SelState.SEL_AE_START);
                        }
                    }
                    break;

                case WM_LBUTTONUP:
                    Selecting = false;
                    break;

                default:
                    break;
            }
            return returnCode;
        }

        private static bool initialized;

        private static IntPtr prevWndProc;

        private static WndProc hookProcDelegate;

        private delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    }

    public class Composition
    {
        public string text;
        public int caret;

        private const int TextBox_xOffset = 16;
        private const int TextBox_yOffset = 8;

        private const int ChatTextBox_xOffset = 12;
        private const int ChatTextBox_yOffset = 12;

        public void Draw(SpriteBatch spriteBatch)
        {
            //cache text, in case change by ime
            string compStr = text;
            int curSel = Math.Min(compStr.Length, caret);

            ITextBox textBox_ = Game1.keyboardDispatcher.Subscriber as ITextBox;
            TextBox textBox = Game1.keyboardDispatcher.Subscriber as TextBox;

            if (compStr.Length > 0 && textBox != null)
            {
                bool isTextBox = textBox is TextBox;

                int xOffset = isTextBox ? TextBox_xOffset : ChatTextBox_xOffset;
                int yOffset = isTextBox ? TextBox_yOffset : ChatTextBox_yOffset;
                Vector2 DrawOrigin = new Vector2(textBox.X + xOffset, textBox.Y + yOffset);

                if (textBox_ != null)
                {
                    int acpStart = textBox_.GetSelection().Start;
                    DrawOrigin.X = textBox_.GetTextExt(new Acp(acpStart, acpStart)).left;
                }
                else
                    DrawOrigin.X += textBox.Font.MeasureString(textBox.Text).X;

                //devide the compstr by compsel
                string left = compStr.Substring(0, curSel);
                string right = compStr.Substring(curSel, compStr.Length - curSel);
                //measure len
                Vector2 vec_left = textBox.Font.MeasureString(left);
                Vector2 vec_right = textBox.Font.MeasureString(right);
                //Draw background
                Texture2D Rect = new Texture2D(Game1.game1.GraphicsDevice, 1, 1, false, SurfaceFormat.Color);
                Color[] colors = new Color[1];
                Rect.GetData(colors);
                colors[0] = Color.White;
                Rect.SetData(colors);
                spriteBatch.Draw(Rect, new Rectangle(
                    (int)DrawOrigin.X,
                    (int)DrawOrigin.Y,
                    (int)(vec_left.X + vec_right.X + 10),//plus 10 to make the bounding box a bit larger, or the caret may not visable
                    32),
                    Color.White);
                DrawOrigin.X += 4;//make items in the white background
                //Draw left part
                spriteBatch.DrawString(textBox.Font, left, new Vector2(DrawOrigin.X, DrawOrigin.Y), Color.Black);
                DrawOrigin.X += vec_left.X;
                //Draw caret
                bool caretVisible = DateTime.UtcNow.Millisecond % 1000 >= 500;
                if (caretVisible)
                {
                    spriteBatch.Draw(Game1.staminaRect, new Rectangle((int)DrawOrigin.X, (int)DrawOrigin.Y, 2, 32), Color.Black);
                }
                DrawOrigin.X += 2;//caret width 2
                //Draw right part
                spriteBatch.DrawString(textBox.Font, right, new Vector2(DrawOrigin.X, (int)DrawOrigin.Y), Color.Black);
            }
        }
    }
}