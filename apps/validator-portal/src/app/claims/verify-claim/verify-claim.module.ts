import { NgModule } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';

import { VerifyClaimComponent } from './verify-claim.component';

@NgModule({
  declarations: [VerifyClaimComponent],
  imports: [CommonModule, FormsModule],
  exports: [VerifyClaimComponent]
})
export class VerifyClaimModule {}
