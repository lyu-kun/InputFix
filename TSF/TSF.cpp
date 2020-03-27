
#include "TSF.h"
#include "TextEdit.h"

TSF::TSF() {
	HRESULT hr = CoInitialize(NULL);//A STA Thread is required or TSF wont work
	CheckHr(hr,"Failed to CoInitialize, need to run at a STA Thread");

	Pin(mgr, ITfThreadMgr*)
	hr = CoCreateInstance(CLSID_TF_ThreadMgr, NULL, CLSCTX_INPROC_SERVER, IID_ITfThreadMgr, (void**)p_mgr);
	CheckHr(hr,"Failed to create ThreadMgr");

	Pin(DocMgr, ITfDocumentMgr*);
	hr = mgr->CreateDocumentMgr(p_DocMgr);
	CheckHr(hr, "Failed to create DocMgr");

	Pin(KeyMgr, ITfKeystrokeMgr*);
	hr = mgr->QueryInterface(IID_ITfKeystrokeMgr, (void**)p_KeyMgr);
	CheckHr(hr, "Failed to query ITfKeystrokeMgr");

	Pin(pump, ITfMessagePump*);
	hr = mgr->QueryInterface(IID_ITfMessagePump, (void**)p_pump);
	CheckHr(hr, "Failed to query ITfMessagePump");
}

TSF::~TSF()
{
	if (DocMgr)
	{
		//pop all of the contexts off of the stack
		DocMgr->Pop(TF_POPF_ALL);

		DocMgr->Release();
		DocMgr = NULL;
	}
	if (context)
	{
		context->Release();
		context = NULL;
	}

	mgr->Release();
	mgr = NULL;

	CoUninitialize();
}

void TSF::Active()
{
	Pin(id, TfClientId);
	HRESULT hr = mgr->Activate(p_id);
	CheckHr(hr, "Failed to Activate");
}

void TSF::Deactive()
{
	mgr->Deactivate();
}

void TSF::CreateContext(_Handle ptr) {
	edit = new TextEdit(ToHWND(ptr));
	Pin(context, ITfContext*);
	Pin(EditCookie, TfEditCookie)
	HRESULT hr = DocMgr->CreateContext(id, 0, edit, p_context, p_EditCookie);
	CheckHr(hr, "Failed to create Context");

	Pin(services, ITfContextOwnerCompositionServices*);
	hr = context->QueryInterface(IID_ITfContextOwnerCompositionServices, (void**)p_services);
	CheckHr(hr, "Failed to query ITfContextOwnerCompositionServices");
}

void TSF::PushContext() {
	//push the context onto the document stack
	HRESULT hr = DocMgr->Push(context);
	CheckHr(hr, "Failed to Push");
}

void TSF::PopContext() {
	HRESULT hr = DocMgr->Pop(TF_POPF_ALL);
	CheckHr(hr, "Failed to Pop");
}

void TSF::ReleaseContext() {
	if (context) {
		context->Release();
	}
	if (edit)
		edit->Release();
}

void TSF::SetTextExt(int left, int right, int top, int bottom) {
	edit->SetTextBoxRect(left, right, top, bottom);
}

void TSF::SetEnable(bool enable) {
	edit->SetEnable(enable);
}

void TSF::ClearText()
{
	edit->ClearText();
}

void TSF::SetCaretX(int x)
{
	edit->SetCaret_X(x);
}

void TSF::SetFocus() {
	mgr->SetFocus(DocMgr);
}

void TSF::AssociateFocus(_Handle hwnd) {
	ITfDocumentMgr* prev_DocMgr;
	mgr->AssociateFocus(ToHWND(hwnd),DocMgr, &prev_DocMgr);
	if (prev_DocMgr && prev_DocMgr != DocMgr) {
		prev_DocMgr->Release();
	}
}

void TSF::TerminateComposition()
{
	services->TerminateComposition(NULL);//Passing Null to terminate all composition
}

void TSF::PumpMsg(_Handle hwnd)
{
	MSG msg;
	BOOL    fResult = TRUE;
	while (SUCCEEDED(pump->PeekMessage(&msg, ToHWND(hwnd), 0, 0, PM_REMOVE, &fResult)) && fResult) 
	{
		BOOL    fEaten;
		if (WM_KEYDOWN == msg.message)
		{
			// does an ime want it?
			if (KeyMgr->TestKeyDown(msg.wParam, msg.lParam, &fEaten) == S_OK && fEaten &&
				KeyMgr->KeyDown(msg.wParam, msg.lParam, &fEaten) == S_OK && fEaten)
			{
				continue;
			}
		}
		else if (WM_KEYUP == msg.message)
		{
			// does an ime want it?
			if (KeyMgr->TestKeyUp(msg.wParam, msg.lParam, &fEaten) == S_OK && fEaten &&
				KeyMgr->KeyUp(msg.wParam, msg.lParam, &fEaten) == S_OK && fEaten)
			{
				continue;
			}
		}

		if (WM_QUIT == msg.message)
		{
			PostMessage(ToHWND(hwnd), msg.message, msg.wParam, msg.lParam);
			return;
		}

		if (fResult)
		{
			TranslateMessage(&msg);
			DispatchMessage(&msg);
		}
	}
	PostMessage(ToHWND(hwnd), WM_NULL, 0, 0);//Prevent window from waiting msg
}
