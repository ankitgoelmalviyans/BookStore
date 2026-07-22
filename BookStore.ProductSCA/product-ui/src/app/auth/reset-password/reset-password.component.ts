import { Component } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { RouterLink } from '@angular/router';
import { MatCardModule } from '@angular/material/card';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatButtonModule } from '@angular/material/button';
import { AuthService } from '../../core/services/auth.service';

@Component({
  selector: 'app-reset-password',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule, RouterLink, MatCardModule, MatFormFieldModule, MatInputModule, MatButtonModule],
  template: `
  <mat-card>
    <h2>Reset Password</h2>
    <form [formGroup]="resetForm" (ngSubmit)="reset()" *ngIf="!submitted">
      <mat-form-field appearance="fill">
        <mat-label>Username</mat-label>
        <input matInput formControlName="username" required>
      </mat-form-field>
      <mat-form-field appearance="fill">
        <mat-label>New Password</mat-label>
        <input matInput type="password" formControlName="newPassword" required>
      </mat-form-field>
      <button mat-raised-button color="primary">Reset Password</button>
    </form>
    <p *ngIf="submitted">{{ message }}</p>
    <a routerLink="/login">Back to login</a>
  </mat-card>
  `
})
export class ResetPasswordComponent {
  resetForm = this.fb.group({ username: [''], newPassword: [''] });
  submitted = false;
  message = '';

  constructor(private fb: FormBuilder, private auth: AuthService) {}

  reset() {
    this.auth.resetPassword(this.resetForm.value).subscribe({
      next: (res: any) => {
        this.submitted = true;
        this.message = res?.message ?? 'If that account exists, its password has been reset and it now requires administrator reactivation.';
      },
      error: () => {
        this.submitted = true;
        this.message = 'Something went wrong. Please try again.';
      }
    });
  }
}
