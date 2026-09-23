import { TestBed } from '@angular/core/testing';
import { of, throwError } from 'rxjs';
import { vi } from 'vitest';
import { DocumentService } from '../document-list/document.service';
import { DocumentStatus } from '../document-list/document.model';
import { UploadDocument } from './upload-document';

async function setup(upload: (file: File) => unknown) {
  await TestBed.configureTestingModule({
    imports: [UploadDocument],
    providers: [{ provide: DocumentService, useValue: { upload } }],
  }).compileComponents();

  const fixture = TestBed.createComponent(UploadDocument);
  await fixture.whenStable();
  return fixture;
}

describe('UploadDocument', () => {
  it('disables Upload until a file is selected', async () => {
    const fixture = await setup(() => of({}));
    const button = fixture.nativeElement.querySelector('button') as HTMLButtonElement;

    expect(button.disabled).toBe(true);

    fixture.componentInstance.selectedFile.set(new File(['x'], 'a.txt'));
    await fixture.whenStable();
    expect(button.disabled).toBe(false);
  });

  it('uploads the selected file and reports success', async () => {
    const upload = vi.fn(() => of({ status: DocumentStatus.Processing }));
    const fixture = await setup(upload);
    const file = new File(['x'], 'a.txt');
    fixture.componentInstance.selectedFile.set(file);

    fixture.componentInstance.upload();
    await fixture.whenStable();

    expect(upload).toHaveBeenCalledWith(file);
    expect(fixture.componentInstance.message()).toBe('File uploaded successfully! Status: Processing');
    expect(fixture.componentInstance.selectedFile()).toBeNull();
  });

  it('reports failure and keeps the file when the upload fails', async () => {
    const fixture = await setup(() => throwError(() => new Error('boom')));
    fixture.componentInstance.selectedFile.set(new File(['x'], 'a.txt'));

    fixture.componentInstance.upload();

    expect(fixture.componentInstance.message()).toBe('File upload failed!');
    expect(fixture.componentInstance.selectedFile()).not.toBeNull();
    expect(fixture.componentInstance.uploading()).toBe(false);
  });
});
